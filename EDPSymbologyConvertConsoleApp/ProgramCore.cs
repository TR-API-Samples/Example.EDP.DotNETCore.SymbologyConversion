﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using Newtonsoft.Json.Linq;
using Refinitiv.EDP.Example.AuthOauth2;
using Refinitiv.EDP.Example.Symbology.Convert;

namespace EDPSymbologyConvertConsoleApp
{
    internal partial class Program
    {
        private const string ParamName =
            "symbologyClient.PostConvertAsync(convertRequest,null).GetAwaiter().GetResult()";

        /// <summary> There is a loop inside the function to ask user to enter EDP username and password until it get a valid token.
        /// User can press Ctrl+c to exit from the loop and exit the application</summary>
        /// <return>True if login success and False if user cancelled the login</return> 
        /// <param name="appConfig"> Required appConfig to read config parameters. </param>
        /// <param name="authToken"> Application has to pass Tokenresponse object to the function and
        /// the function will return Tokenresponse to application. It could be null if user cancelled the login </param>
       
        public static bool DoLoginAndGetToken(out Tokenresponse authToken, Config appConfig)
        {
            authToken = null;
            var bCancelledLogin = false;
            var cts = new CancellationTokenSource();
            Console.TreatControlCAsInput = false;
            Console.CancelKeyPress += (s, ev) =>
                {
                    bCancelledLogin = true;
                    ev.Cancel = true;
                    cts.Cancel();

                };
            do
            {
               

                Console.WriteLine("\nSignin to RDP(Refinitiv Data Platform) Press Ctrl+C to cancel");
                Console.WriteLine("=============================");


                if (bCancelledLogin) break;

                if (string.IsNullOrEmpty(appConfig.Username))
                {
                   

                    Console.Write("Machine ID or Username(Email):");
                    appConfig.Username = Console.ReadLine();
                }
                else
                {
                    Console.WriteLine($"Machine ID or Username(Email):{appConfig.Username}");
                }
                //if (!RegexUtilities.IsValidEmail(appConfig.Username))
                //{
                    //assume that client use machine ID and assign machine id to client id.
                //    appConfig.ClientId = appConfig.Username;
               //}
                //else
                //{
                if (string.IsNullOrEmpty(appConfig.ClientId))
                {
                    Console.Write("Enter Client ID/AppKey:");
                    appConfig.ClientId = Console.ReadLine();
                }
                else
                {
                    Console.WriteLine($"Client ID:{appConfig.ClientId}");
                }
                //}

                if (!bCancelledLogin && string.IsNullOrEmpty(appConfig.RefreshToken) && string.IsNullOrEmpty(appConfig.Password))
                {
                    Console.Write("Enter Password:");
                    appConfig.Password = ReadPassword();
                }

                Console.WriteLine("=============================");

                if (bCancelledLogin) break;

                Console.WriteLine("Logging in to the EDP server, please wait");

                using (var client = new HttpClient(GenerateHttpClientHandler(appConfig)))
                {
                    var authClient = new AuthorizeClient(client);

                    //If user specify authorize token url vi app config, it overrides default authorize url.
                    if (!string.IsNullOrEmpty(appConfig.AuthBaseURL))
                        authClient.BaseUrl = appConfig.AuthBaseURL;

                    try
                    {
                        authToken = string.IsNullOrEmpty(appConfig.RefreshToken)
                                        ? GetNewToken(appConfig.Username, appConfig.Password,appConfig.ClientId, authClient, cts.Token)
                                        : RefreshToken(
                                            appConfig.Username,
                                            appConfig.RefreshToken,
                                            authClient,
                                            cts.Token);
                    }
                    catch (EDPAuthorizeException<AuthError> exception)
                    {
                        Console.WriteLine(
                            $"Login Failed! Status Code:{exception.StatusCode} "
                            + $"Error:{exception.Result.Error1} {exception.Result.Error_description} {exception.Result.Error_uri}");


                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"\nGet {exception.GetType().Name} Error {exception.Message}");
                    }
                    finally
                    {
                        //reset everything to empty and ask user to enter credential again.
                        appConfig.Username = string.Empty;
                        appConfig.Password = string.Empty;
                        appConfig.RefreshToken = string.Empty;
                        appConfig.ClientId = string.Empty;
                        //Console.WriteLine("\nRe-enter EDP username and password or press Ctrl+C to exit");
                    }

                }
            } while (!bCancelledLogin && (authToken == null));

            return !bCancelledLogin && (authToken != null);
        }
        /// <summary> Call PostConvertAsync which internally use Http Post to get data form Symbology Conversion service</summary>
        /// <param name="symbologyData">EDPSymbologyClient object</param>
        /// <param name="appConfig"> appConfig object to verify option tot print Json request message </param>
        /// <param name="convertRequest"> ConvertRequest object contains request values</param>
        /// <param name="cts"> CancellationTokenSource object </param>
        public static Symbology SymbologyConvertPost(EDPSymbologyClient symbologyClient,
            ConvertRequest convertRequest,
            Config appConfig,
            CancellationTokenSource cts)
        {
            if (appConfig.Verbose)
                PrintConvertRequest(convertRequest);

            var symbologyResponse =
                symbologyClient.PostConvertAsync(convertRequest,Format.NoMessages,cts.Token).GetAwaiter().GetResult() ??
                throw new ArgumentNullException(
                    ParamName);
            return symbologyResponse;
        }
        /// <summary> Call GetConvertAsync which internally use Http Get to get data form Symbology Conversion service</summary>
        /// <param name="symbologyData">EDPSymbologyClient object</param>
        /// <param name="appConfig"> appConfig object to verify option tot print Json request message </param>
        /// <param name="convertRequest"> ConvertRequest object contains request values</param>
        /// <param name="cts"> CancellationTokenSource object </param>
        public static Symbology SymbologyConvertGet(EDPSymbologyClient symbologyClient,
            ConvertRequest convertRequest,
            Config appConfig,
            CancellationTokenSource cts)
        {
            if (appConfig.Verbose)
                PrintConvertRequest(convertRequest);

            var symbolResult = symbologyClient.GetConvertAsync(string.Join(',', convertRequest.Universe),
                    convertRequest.To,Format.NoMessages,cts.Token)
                .GetAwaiter().GetResult();

            return symbolResult;
        }
        /// <summary> Print contents inside the Symbology object returned from Symbology Conversion service.
        /// Also export the data and headers from Symbology object to .CSV file.</summary>
        /// <param name="symbologyData">Symbolgoy object</param>
        /// <param name="appConfig"> Application config object to verify options for print output and export csv file</param>
       
        public static void PrintSymbologyResponse(Symbology symbologyData, Config appConfig)
        {
            if (symbologyData is null) return;

            if (appConfig.Verbose)
            {
                Console.WriteLine();
                Console.WriteLine("============= Response Body in Json Format=================");
                Console.WriteLine(symbologyData.ToJson());
                Console.WriteLine("===========================================================");
                Console.WriteLine();
            }

            Console.WriteLine("===========Universe List=================");
            foreach (var universe in symbologyData.Universe)
            {
                Console.WriteLine($"Common Name:{universe.Common_Name}");
                Console.WriteLine($"Instrument:{universe.Instrument}");
                Console.WriteLine($"Organization Perm ID:{universe.OrganizationPermID}");
                Console.WriteLine($"Reporting Currency:{universe.ReportingCurrentcy}");
            }
            Console.WriteLine("=========================================");

            Console.WriteLine();
            Console.WriteLine($"Row Count: {symbologyData.Links.Count}");
            Console.WriteLine("===============Header==================");
            foreach (var header in symbologyData.Headers)
                Console.Write($"| {header.Title}({header.Name}) ");
            Console.WriteLine("\n=======================================");

            foreach (var row in symbologyData.Data)
            {
                foreach (var column in row)
                    Console.Write(column != null ? $"|{column}" : "|null ");

                Console.WriteLine("\n");
            }

            if (symbologyData.Messages != null)
            {
                Console.WriteLine("\tMessage");
                Console.WriteLine("\t\tCodes");
                foreach (var codeList in symbologyData.Messages.Codes)
                {
                    Console.WriteLine("\t\t [");
                    int index=0;
                    foreach (var code in codeList)
                    {
                        Console.Write($"\t\t {code}");
                        if (index < codeList.Count-1)
                        {
                            index++;
                            Console.WriteLine(",");
                        }
                       
                    }

                    Console.WriteLine("\n\t\t ]");
                }

                Console.WriteLine("\t\tDescription");
                foreach (var description in symbologyData.Messages.Descriptions)
                {
                    Console.WriteLine($"\t\t\tCode:{description.Code}");
                    Console.WriteLine($"\t\t\tDescription:{description.Description}");
                }
                Console.WriteLine("\t\tAttributes");
                
            }

            Console.WriteLine("=======================================");

            if (!appConfig.ExportToCSV) return;
            Console.WriteLine();
            Console.WriteLine($"Export the data to CSV file {appConfig.CsvFilePath}");
            try
            {
                using (var streamWriter = new StreamWriter(appConfig.CsvFilePath))
                {
                    if (ExportSymbologyConvertDataToCSV(symbologyData, streamWriter))
                        Console.WriteLine($"Writing data to {appConfig.CsvFilePath} complete");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{exception.GetType()} : {exception.Message}");
            }
        }
        /// <summary>
        /// Use this function to Refresh the Access Token
        /// </summary>
        /// <param name="username"> EDP username</param>
        /// <param name="refreshToken">The refresh token</param>
        /// <param name="client">AuthorizeClient object to call TokenAsync</param>
        /// <param name="cts">CancellationToken</param>
        /// <returns></returns>
        public static Tokenresponse RefreshToken(string username, string refreshToken, AuthorizeClient client,
            CancellationToken cts)
        {
            Tokenresponse tokenResponse = null;
            tokenResponse = client.TokenAsync("refresh_token", username, "", "", "", refreshToken,
                username, "",
                "", "", "", cts).GetAwaiter().GetResult().Result;
            return tokenResponse;
        }
        /// <summary>
        /// Use this function to get a New AccessToken+Refresh token
        /// </summary>
        /// <param name="username">EDP Username</param>
        /// <param name="password">EDP Password</param>
        /// <param name="client">Authorization Client object</param>
        /// <param name="cancellationToken">CancellationToken object</param>
        /// <returns></returns>
        public static Tokenresponse GetNewToken(string username, string password,string clientId, AuthorizeClient client,
            CancellationToken cancellationToken)
        {
            var tokenResult = client
                .TokenAsync("password", username, password, "", "trapi", "",clientId, "", "true", "",
                    "", cancellationToken)
                .GetAwaiter().GetResult();
            return tokenResult.Result;
        }
        /// <summary>
        /// This function read request parameter form appConfig and convert comma separate value to list and set ti back to ConvertRequest object.
        /// It also verify enum value from To parameter if it contains invalid value it automatically remove the value from field list before set it ConvertRequest object.
        /// </summary>
        /// <param name="appConfig"></param>
        /// <returns>ConvertRequest</returns>
        private static ConvertRequest ValidateAndCreateConvertRequest(Config appConfig)
        {
            var convertRequest = new ConvertRequest();
            try
            {
                if (appConfig.UseJsonRequestFile)
                {
                    Console.WriteLine($"Reading and Processing request message from {appConfig.JsonRequestFile} file");
                    //Parse Universe list

                    var jObject = JObject.Parse(File.ReadAllText(appConfig.JsonRequestFile));
                    convertRequest.Universe = jObject["universe"] != null
                        ? jObject["universe"].Select(a => (string) a).ToList()
                        : new List<string>();

                    var toList = jObject["to"] != null
                        ? jObject["to"].Select(a => (string) a).ToList()
                        : new List<string>();
                    //Parse To list and validate the enum value 
                    var errorMsg = string.Empty;
                    convertRequest.To = toList.Count > 0
                        ? EDPSymbologyClient.ConvertStringToEnumList(toList, out errorMsg)
                        : new List<FieldEnum>();

                    if (!string.IsNullOrEmpty(errorMsg)) Console.WriteLine(errorMsg);
                }
                else
                {
                    convertRequest.Universe = appConfig.Universe != null
                        ? appConfig.Universe.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList()
                        : new List<string>();

                    var validateError = string.Empty;
                    var toList = appConfig.ToEnum != null
                        ? appConfig.ToEnum.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList()
                        : new List<string>();

                    convertRequest.To = toList.Count > 0
                        ? EDPSymbologyClient.ConvertStringToEnumList(toList, out validateError)
                        : new List<FieldEnum>();
                    if (!string.IsNullOrEmpty(validateError)) Console.WriteLine(validateError);
                }

                if (!string.IsNullOrEmpty(appConfig.UniverseListFilePath))
                {
                    var lines = File.ReadLines(appConfig.UniverseListFilePath);
                    var universeList = new List<string>();
                    foreach (var line in lines)
                        if (!string.IsNullOrEmpty(line.Trim()) && universeList.Count < 99)
                            universeList.Add(line);

                    convertRequest.Universe = universeList;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Unable to convert json data to request message {exception.Message}");
                return convertRequest;
            }

            return convertRequest;
        }

        private static void PrintConvertRequest(ConvertRequest convertRequest)
        {
            if (convertRequest == null)
            {
                Console.WriteLine("ConvertRequest should not be null");
                return;
            }

            Console.WriteLine("======== JSon Request Body =======");
            Console.WriteLine(convertRequest.ToJson());
            Console.WriteLine("==================================");
        }

        private static void DumpAppConfig(Config appConfig, ConvertRequest convertRequest)
        {
            if (convertRequest == null || appConfig==null)
            {
                Console.WriteLine("Unable to print configuration because Config or ConvertRequest object is null");
                return;
            }

            Console.WriteLine("========= Application Configuration List =============");
            Console.WriteLine("Universe:{0}",
                convertRequest.Universe != null ? string.Join(",", convertRequest.Universe) : "");
            Console.WriteLine("ToEnum:{0}",
                (convertRequest.To != null)&&(convertRequest.To.Count>0) ? string.Join(",", convertRequest.To) : "No valid field list pass to application, returns all fields instead");
            Console.WriteLine($"Use Json File:{appConfig.JsonRequestFile} {appConfig.UseJsonRequestFile}");
            Console.WriteLine($"Export to CSV File:{appConfig.CsvFilePath} {appConfig.ExportToCSV} ");
            if(!string.IsNullOrEmpty(appConfig.AccessToken))Console.WriteLine($"Username or Client ID (hidden):{appConfig.Username}");
            if (!string.IsNullOrEmpty(appConfig.AccessToken)) Console.WriteLine($"AccessToken(hidden):{appConfig.AccessToken}");
            if (!string.IsNullOrEmpty(appConfig.RefreshToken)) Console.WriteLine($"RefreshToken(hidden):{appConfig.RefreshToken}");
            if (!string.IsNullOrEmpty(appConfig.SymbologyBaseURL)) Console.WriteLine($"Symbology Base URL(hidden):{appConfig.SymbologyBaseURL}");
            if (!string.IsNullOrEmpty(appConfig.AuthBaseURL)) Console.WriteLine($"Authorization Base URL(hidden):{appConfig.AuthBaseURL}");
            if (!string.IsNullOrEmpty(appConfig.ProxyServer)) Console.WriteLine($"ProxyServer(hidden):{appConfig.ProxyServer}");
            Console.WriteLine($"UseProxyServer(hidden):{appConfig.UseProxyServer}");
            Console.WriteLine("======================================================");
        }

        private static bool ExportSymbologyConvertDataToCSV(Symbology data, StreamWriter writer)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            if (writer == null)
                throw new ArgumentNullException("writer");

            try
            {
                using (var csv = new CsvWriter(writer))
                {
                    // Write CSV Header first
                    foreach (var header in data.Headers)
                        csv.WriteField(header.Title);
                    csv.NextRecord();
                    csv.Flush();

                    // Write each row from Json data
                    foreach (var rowData in data.Data)
                    {
                        foreach (var symbolValue in rowData)
                            if (symbolValue != null)
                                csv.WriteField(symbolValue.ToString());
                            else
                                csv.WriteField("NULL");

                        csv.NextRecord();
                        csv.Flush();
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Exception when generating CSV file\n {exception.Message}");
            }

            return true;
        }
        /// <summary>
        /// Generate Http Handler based on Config parameters
        /// </summary>
        /// <param name="appConfig"></param>
        /// <returns>HttpClientHandler</returns>
        private static HttpClientHandler GenerateHttpClientHandler(Config appConfig)
        {
            var httpHandler = new HttpClientHandler();
            httpHandler.UseProxy = appConfig.UseProxyServer;
            if (appConfig.UseProxyServer)
            {
                httpHandler.Proxy = new WebProxy(appConfig.ProxyServer, false);

                if (!string.IsNullOrEmpty(appConfig.ProxyUsername) && !string.IsNullOrEmpty(appConfig.ProxyPassword))
                {
                    httpHandler.Credentials = new NetworkCredential(
                        appConfig.ProxyUsername,
                        appConfig.ProxyPassword);
                }

                httpHandler.UseDefaultCredentials = string.IsNullOrEmpty(appConfig.ProxyUsername)
                                                    && string.IsNullOrEmpty(appConfig.ProxyPassword);

            }

            return httpHandler;
        }
        private static void DumpToken(Tokenresponse token)
        {
            if (token == null)
            {
                Console.WriteLine("It's null token object");
                return;
            }

            Console.WriteLine("*************Token Details**************");
            Console.WriteLine($"AccessToken={token.Access_token}\n" +
                              $"Expired={token.Expires_in}\n" +
                              $"RefreshToken={token.Refresh_token}\n" +
                              $"Scope={token.Scope}\n" +
                              $"TokenType={token.Token_type}");
            Console.WriteLine("****************************************");
        }
    }
}