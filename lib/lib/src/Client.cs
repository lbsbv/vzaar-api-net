﻿using System;
using System.Net;
using System.IO;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections;
using System.Collections.Generic;

namespace VzaarApi
{
	public class Client
	{

		public static string url = "https://api.vzaar.com/api/";
		public static string client_id = "id";
		public static string auth_token = "token";
		public static string version = "v2";
		public static bool urlAuth = false;

		public static readonly string UPLOADER = "Vzaar .NET SDK";
		public static readonly string VERSION = "2.0.0-alpha";

		public static readonly long ONE_MB = (1024 * 1024);
		public static readonly long MULTIPART_MIN_SIZE = (5 * ONE_MB);

		internal HttpClient httpClient;
		public HttpResponseHeaders httpHeaders { get; internal set;}

		public string CfgUrl { get; set;}
		public string CfgVerson { get; set;}
		public bool CfgUrlAuth { get; set;}
		public string CfgClientId { get; set; }
		public string CfgAuthToken { get; set;}

		public delegate void UploadProgressHandler (object sender, VideoUploadProgressEventArgs e);
		public event UploadProgressHandler UploadProgress = delegate { };

		public Client() {
			httpClient = new HttpClient ();

			//initialize 
			CfgUrl = Client.url;
			CfgVerson = Client.version;
			CfgUrlAuth = Client.urlAuth;
			CfgClientId = Client.client_id;
			CfgAuthToken = Client.auth_token;
		}

		public TimeSpan HttpTimeout {
			set { httpClient.Timeout = value; }
			get { return httpClient.Timeout;  }
		}

		public static Client GetClient() {
			return new Client ();
		}

		public string RateLimit {

			get { return CheckRates ("X-RateLimit-Limit"); }
		}

		public string RateRemaining {

			get { return CheckRates ("X-RateLimit-Remaining"); }
		}

		public string RateReset {

			get { return CheckRates ("X-RateLimit-Reset"); }
		}

		internal string CheckRates(string key){

			IEnumerable<string> header;

			string value = string.Empty;
			if (httpHeaders.TryGetValues (key, out header)) {
				value = ((List<string>)header) [0];
			}
			return value;
		}

		internal async Task<string> HttpGetAsync(string endpoint, Dictionary<string, string> query) {

			Uri httpUri = BuildUri (endpoint);
			httpUri = BuildQuery (httpUri, query);

			HttpRequestMessage msg = new HttpRequestMessage ();
			msg.RequestUri = httpUri;
			msg.Method = HttpMethod.Get;

			var jsonResponse = await HttpSendAsync (msg);

			return jsonResponse;
		}

		internal async Task<string> HttpPostAsync(string path, string body) {

			Uri httpUri = BuildUri (path);

			HttpRequestMessage msg = new HttpRequestMessage ();
			msg.RequestUri = httpUri;
			msg.Method = HttpMethod.Post;

			StringContent content = new StringContent (body);
			content.Headers.ContentType = new MediaTypeHeaderValue ("application/json");

			msg.Content = content;

			var jsonResponse = await HttpSendAsync (msg);

			return jsonResponse;
		}

		internal async Task<string> HttpPatchAsync(string path, string body) {

			Uri httpUri = BuildUri (path);

			HttpRequestMessage msg = new HttpRequestMessage ();
			msg.RequestUri = httpUri;
			msg.Method = new HttpMethod("PATCH");

			StringContent content = new StringContent (body);
			content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

			msg.Content = content;

			var jsonResponse = await HttpSendAsync (msg);

			return jsonResponse;

		}

		internal async Task HttpDeleteAsync(string path) {

			Uri httpUri = BuildUri (path);

			HttpRequestMessage msg = new HttpRequestMessage ();
			msg.RequestUri = httpUri;
			msg.Method = HttpMethod.Delete;

			await HttpSendAsync (msg);

			//if the request completed, means successful
			//no return value needed
		}

		internal async virtual Task<string> HttpSendAsync(HttpRequestMessage msg) {

			if (CfgUrlAuth == true) {

				Dictionary<string, string> query = new Dictionary<string, string> ();
				query.Add ("client_id", client_id);
				query.Add ("auth_token", auth_token);

				Uri uri = BuildQuery (msg.RequestUri, query);
				msg.RequestUri = uri;

			} else {

				msg.Headers.Add ("X-Client-Id", client_id);
				msg.Headers.Add ("X-Auth-Token", auth_token);

			}

			#if DEBUG 
			Debug.WriteLine ("\n\nRequest Method");
			Debug.WriteLine (msg.Method.ToString());
			Debug.WriteLine ("Request Uri");
			Debug.WriteLine (msg.RequestUri.AbsoluteUri);
			Debug.WriteLine ("Request Headers");
			Debug.WriteLine (msg.Headers.ToString().Trim());

			if (msg.Content != null) {
				Debug.WriteLine ("Request Content");
				string debug_content = await msg.Content.ReadAsStringAsync ();
				Debug.WriteLine (debug_content);
			}
			#endif

			HttpResponseMessage response = await this.httpClient.SendAsync (msg);

			ValidateHttpResponse (response);

			httpHeaders = response.Headers;

			var bodyResponse = await response.Content.ReadAsStringAsync ();

			return bodyResponse;
		}

		internal void ValidateHttpResponse(HttpResponseMessage response) {

			#if DEBUG 
			Debug.WriteLine ("Response StatusCode");
			Debug.WriteLine (response.StatusCode + ": "+ (int)response.StatusCode);
			Debug.WriteLine ("Response Headers");
			Debug.WriteLine (response.Headers.ToString().Trim());

			if (response.Content != null) {
				Debug.WriteLine ("Response Content");
				var task = response.Content.ReadAsStringAsync ();
				task.Wait();
				string debug_content = task.Result;
				Debug.WriteLine (debug_content);
			}
			#endif

			switch (response.StatusCode) {
			case HttpStatusCode.OK:
			case HttpStatusCode.Created:
			case HttpStatusCode.NoContent:
				break;
			case HttpStatusCode.BadRequest:
			case HttpStatusCode.Unauthorized:
			case HttpStatusCode.Forbidden:
			case HttpStatusCode.NotFound:
			case (HttpStatusCode)422: //Unprocessable Entity
			case (HttpStatusCode)429: //Too many Requests
			case HttpStatusCode.InternalServerError:

				var task = response.Content.ReadAsStringAsync ();
				task.Wait ();

				var error = task.Result;

				string message = "StatusCode: " + response.StatusCode.ToString () + "\r\n";
				message += error;

				throw new VzaarApiException (message);

			default:
				string unknown = "Unknown response. StatusCode: " + response.StatusCode.ToString ();
				throw new VzaarApiException (unknown);
			}
		}

		internal async Task HttpPostS3Async(string filepath, Signature signature) {
			FileInfo file = new FileInfo(filepath);

			if (file.Exists == false) {
				throw new VzaarApiException ("File does not exsist: "+filepath);
			}

			string hostname = (string)signature ["upload_hostname"];

			//move signature to Dictionary
			Dictionary<string, string> postFields = new Dictionary<string, string>();

			postFields.Add ("x-amz-meta-uploader", Client.UPLOADER + Client.VERSION);
			postFields.Add ("AWSAccessKeyId",(string)signature["access_key_id"]);
			postFields.Add ("Signature",(string)signature["signature"]);
			postFields.Add ("acl",(string)signature["acl"]);
			postFields.Add ("bucket",(string)signature["bucket"]);
			postFields.Add ("policy",(string)signature["policy"]);
			postFields.Add ("success_action_status",(string)signature["success_action_status"]);

			string key = (string)signature["key"];
			string fileKey = "";
			if(String.IsNullOrEmpty(key) == false)
				fileKey = key.Replace ("${filename}", file.Name);

			postFields.Add ("key",fileKey);


			long? parts = signature ["parts"] as long?;

			if (parts == null) {

				//upload file in POST form
				FileStream fileStream;
				using(fileStream = new FileStream (filepath, FileMode.Open, FileAccess.Read)) {

					await HttpPostMfdcAsync (hostname, file.Name, postFields, fileStream);
					// response is validated in the HttpPostMfdcAsync
				}

			} else {

				string keyCache = postFields["key"];

				long chunk = 0;

				if (signature ["part_size_in_bytes"] == null)
					throw new VzaarApiException ("File Upload: not valid 'part_size_in_bytes'");

				int chunkSize = Convert.ToInt32((long)signature ["part_size_in_bytes"]);
				byte[] buffer = new byte[chunkSize];

				int bytesCount;

				FileStream fileStream;
				using (fileStream = new FileStream (filepath, FileMode.Open, FileAccess.Read)) {
					
					while ((bytesCount = await fileStream.ReadAsync(buffer,0,chunkSize)) != 0 ) {

						string chunkKey = keyCache + "." + chunk.ToString ();
						postFields.Remove ("key");
						postFields.Add ("key", chunkKey);
						
						MemoryStream chunkStream;
						using (chunkStream = new MemoryStream (buffer,0,bytesCount)) {

							await HttpPostMfdcAsync (hostname, file.Name, postFields, chunkStream);

							// response is validated in the HttpPostMfdcAsync
						}
							
						VideoUploadProgressEventArgs progress = new VideoUploadProgressEventArgs () {
							totalParts = (long)parts,
							uploadedChunk = chunk
						};

						UploadProgress (this, progress);
							
						chunk++;
					}
				}
					
			}

		}

		internal virtual async Task HttpPostMfdcAsync( string hostname, string filename, Dictionary<string,string> fields, Stream stream) {

			HttpRequestMessage msg = new HttpRequestMessage ();
			msg.RequestUri = new Uri(hostname);
			msg.Method = HttpMethod.Post;

			MultipartFormDataContent mfdc = new MultipartFormDataContent ();

			//upload signature data in POST form
			StringContent postField;

			Debug.WriteLine ("\n\nRequest Method");
			Debug.WriteLine (msg.Method.ToString());
			Debug.WriteLine ("Request Uri");
			Debug.WriteLine (msg.RequestUri.AbsoluteUri);
			Debug.WriteLine ("Request Headers");
			Debug.WriteLine (msg.Headers.ToString().Trim());
			Debug.WriteLine ("Request Content");

			foreach (var item in fields) {
				postField = new StringContent (item.Value);
				mfdc.Add (postField, item.Key);

				Debug.WriteLine (item.Key + ": " + item.Value);
			}

			StreamContent postFile = new StreamContent (stream);
			postFile.Headers.ContentType = new MediaTypeHeaderValue("binary/octet-stream");
			mfdc.Add (postFile, "file", filename);

			Debug.WriteLine ("file: " + filename);

			msg.Content = mfdc;

			var response = await this.httpClient.SendAsync (msg);

			ValidateS3Response (response);
		}

		internal void ValidateS3Response(HttpResponseMessage response) {

			string bodyResponse = "";

			if (response.Content != null) {
				var task = response.Content.ReadAsStringAsync ();
				task.Wait ();
				bodyResponse = task.Result;
			}

			Debug.WriteLine ("Response StatusCode");
			Debug.WriteLine (response.StatusCode + ": "+ (int)response.StatusCode);
			Debug.WriteLine ("Response Headers");
			Debug.WriteLine (response.Headers.ToString().Trim());
			Debug.WriteLine ("Response Content");
			Debug.WriteLine (bodyResponse);


			switch (response.StatusCode) {
			case HttpStatusCode.Created:
				break;
			default:
				string message = "StatusCode: " + response.StatusCode.ToString () + "\r\n";
				message += bodyResponse;

				throw new VzaarApiException (message);
			}
			
		}

		internal Uri BuildUri (string endpoint) {

			//build uri base
			string uri = CfgUrl.TrimEnd('/') + "/" + CfgVerson;
			//add endpoint
			uri += "/" + endpoint;

			return new Uri (uri);

		}

		internal Uri BuildQuery(Uri uri, Dictionary<string, string> query) {

			UriBuilder reqUrl = new UriBuilder (uri);

			if (query != null) {

				string queryString = "";
				foreach (string key in query.Keys) {
					string item = key + "=" + query [key];

					if (queryString == "")
						queryString = item;
					else
						queryString += "&" + item;
				}

				if (reqUrl.Query != null && reqUrl.Query.Length > 1)
					reqUrl.Query = reqUrl.Query.Substring (1) + "&" + queryString;
				else
					reqUrl.Query = queryString;
			}

			return reqUrl.Uri;

		}

	}//end class
}//end namespace 