#region usings
using System;
using System.IO;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

using VVVV.Core.Logging;

using System.Threading;
using System.Net;
using System.Collections.Concurrent;

#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "HTTPListener", Category = "Raw", Help = "", Tags = "Network")]
	#endregion PluginInfo
	public class RawHTTPListenerNode : IPluginEvaluate, IPartImportsSatisfiedNotification, IDisposable
	{
		#region fields & pins
		[Input("Address", IsSingle = true, DefaultString = "0.0.0.0:8080")]
		public IDiffSpread<string> Address;
		
		[Input("Enable")]
		public IDiffSpread<bool> Enable;
		
		[Output("Output")]
		public ISpread<HttpListenerContext> Contexts;
		
		[Output("Paths")]
		public ISpread<string> RoutesAccepted;
		
		[Output("Number of Accepted Requests")]
		public ISpread<int> NumRequests;
		
		[Output("Listening")]
		public ISpread<bool> FEnabled;
		
		#endregion fields & pins
		
		[Import()]
		ILogger Logger;
		
		HttpListener Server = new HttpListener();
		
		public void Dispose() {
			try {
				Listener.Abort();
			} catch(Exception e) {}
			
			Server.Close();
		}
		
		Thread Listener;
		ConcurrentQueue<HttpListenerContext> ContextQueue = new ConcurrentQueue<HttpListenerContext>();
		
		
		//called when all inputs and outputs defined above are assigned from the host
		public void OnImportsSatisfied()
		{
			
			Listener = new Thread(new ThreadStart(this.Listen));
			Listener.IsBackground = true;
			Listener.Start();
			
			Address.Changed += delegate(IDiffSpread<string> addrs) {
				foreach(string s in addrs) {
					try {
						Server.Prefixes.Clear();
						Server.Prefixes.Add("http://"+s+"/");
					} catch(System.ObjectDisposedException  de) {
						Server = new HttpListener();
						Server.Prefixes.Add("http://"+s+"/");
					}catch(Exception e) {
						Logger.Log(e);
					}
				}
			};
			
			Enable.Changed += delegate(IDiffSpread<bool> enable) {
				if (enable.SliceCount == 0) return;
				if (enable[0]) {
					Server.Start();
					requests = 0;
				} else {
					Server.Stop();
				}
			};
			
		}
		
		public void Listen() {
			while(true) {
				try {
					HttpListenerContext c = Server.GetContext();
					ContextQueue.Enqueue(c);
					Interlocked.Increment(ref requests);
				} catch(ThreadAbortException tae) {
					return;
				} catch (ThreadInterruptedException tie) {
				} catch (Exception err) {
				} finally {
				}
			}
		}
		
		int requests;
		
		public void Evaluate(int spreadMax)
		{
			FEnabled.SliceCount = 1;
			FEnabled[0] = Server.IsListening;
			NumRequests.SliceCount = 1;
			NumRequests[0] = requests;
			
			Contexts.SliceCount = 0;
			RoutesAccepted.SliceCount = 0;
			HttpListenerContext c;
			while(ContextQueue.TryDequeue(out c)){
				Contexts.Add(c);
				RoutesAccepted.Add(c.Request.Url.AbsolutePath);
			}
			
		}
		
	}
	
	#region PluginInfo
	[PluginInfo(Name = "Reader", Category = "HTTP", Help = "", Tags = "Network")]
	#endregion PluginInfo
	public class RawHTTPReaderNode : IPluginEvaluate, IPartImportsSatisfiedNotification
	{
		#region fields & pins
		[Input("Input")]
		public ISpread<HttpListenerContext> Contexts;
		
		[Output("Output")]
		public ISpread<HttpListenerContext> ContextsOut;
		
		[Output("Request Body")]
		public ISpread<Stream> RequestBody;
		
		[Output("Headers")]
		public ISpread<string> Headers;
		
		[Output("Path")]
		public ISpread<string> Paths;
		
		[Output("Method")]
		public ISpread<string> Methods;
		
		#endregion fields & pins
		
		//called when all inputs and outputs defined above are assigned from the host
		public void OnImportsSatisfied()
		{
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			//			if (Contexts == null || Contexts.SliceCount == 0) {
				//				RequestBody.SliceCount = 0;
			//				return;
			//			}
			
			RequestBody.SliceCount = spreadMax;
			Headers.SliceCount = spreadMax;
			Paths.SliceCount = spreadMax;
			Methods.SliceCount = spreadMax;
			ContextsOut.SliceCount = spreadMax;
			
			for( int i=0; i < spreadMax; i++) {
				try {
					var request = Contexts[i].Request;
					
					RequestBody[i] = new MemoryStream();
					request.InputStream.CopyTo(RequestBody[i]);
					
					Headers[i] = request.Headers.ToString();
					Paths[i] = request.Url.AbsolutePath;
					Methods[i] = request.HttpMethod;
				} catch(Exception e) {}
			}
			
			ContextsOut.AssignFrom(Contexts);
		}
	}
	
	#region PluginInfo
	[PluginInfo(Name = "Writer", AutoEvaluate = true, Category = "HTTP", Help = "", Tags = "Network")]
	#endregion PluginInfo
	public class RawHTTPWriterNode : IPluginEvaluate, IPartImportsSatisfiedNotification
	{
		#region fields & pins
		[Input("Input")]
		public ISpread<HttpListenerContext> Contexts;
		
		[Input("Response Body")]
		public ISpread<Stream> Responses;
		
		[Input("Status", DefaultValue = 200)]
		public ISpread<int> Statuses;
		
		#endregion fields & pins
		
		//called when all inputs and outputs defined above are assigned from the host
		public void OnImportsSatisfied()
		{
		}
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			int i = 0;
			foreach(HttpListenerContext context in Contexts) {
				HttpListenerResponse response = context.Response;
				
				response.StatusCode = Statuses.SliceCount > 0 ? Statuses[i] : 200;
				
				try {
					Stream source = Responses.SliceCount  > 0 ? Responses[i] : Stream.Null;
					source.CopyTo(response.OutputStream);
					source.Seek(0,SeekOrigin.Begin);
				} catch(Exception e) {
					// nothing, maybe append to error spread...
				} finally {
					response.Close();
				}
				
				i++;
			}
		}
	}
	
	#region PluginInfo
	[PluginInfo(Name = "Abort", AutoEvaluate = true, Category = "HTTP", Help = "", Tags = "Network")]
	#endregion PluginInfo
	public class RawHTTPAbortNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input")]
		public ISpread<HttpListenerContext> Contexts;
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			foreach(HttpListenerContext context in Contexts) {
				context.Response.Abort();
			}
		}
	}
	
	#region PluginInfo
	[PluginInfo(Name = "NotFound", AutoEvaluate = true, Category = "HTTP", Help = "", Tags = "Network")]
	#endregion PluginInfo
	public class RawHTTPNotFoundNode : IPluginEvaluate
	{
		#region fields & pins
		[Input("Input")]
		public ISpread<HttpListenerContext> Contexts;
		
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int spreadMax)
		{
			foreach(HttpListenerContext context in Contexts) {
				HttpListenerResponse response = context.Response;
				response.StatusCode = 404;
				response.Close();
			}
		}
	}
	
}
