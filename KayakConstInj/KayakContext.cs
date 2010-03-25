using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Kayak.Async;

namespace Kayak
{
    /// <summary>
    /// Encapsulates the lifecycle of a single HTTP transaction.
    /// </summary>
    public sealed class KayakContext
    {
        internal static readonly string ContextExceptionKey = "ContextException";
        internal Socket Socket { get; private set; }

        /// <summary>
        /// The KayakServer which accepted the underlying connection.
        /// </summary>
        public KayakServer Server { get; private set; }

        /// <summary>
        /// The KayakRequest object for this context.
        /// </summary>
        public KayakRequest Request { get; private set; }

        /// <summary>
        /// The KayakResponse object for this context.
        /// </summary>
        public KayakResponse Response { get; private set; }

        /// <summary>
        /// A handy collection to throw stuff into.
        /// </summary>
        public Dictionary<object, object> Items { get; private set; }

        public IKayakActivator Activator { get; private set;}

        public bool CanLog { get; set; }

        internal KayakContext(KayakServer server, Socket socket)
        {
            Server = server;
            Socket = socket;
            Request = new KayakRequest(socket);
            Response = new KayakResponse(socket);
            Items = new Dictionary<object, object>();
            Activator = new KayakActivator();
        }

        internal IEnumerable<IYieldable> HandleConnection()
        {
            var readHeaders = Request.ReadHeaders().Coroutine();
            yield return readHeaders;

            if (readHeaders.Exception != null)
                throw new Exception("Exception while reading headers.", readHeaders.Exception);

            if (CanLog)
                Console.WriteLine("[{0}] {1} {2} {3}", DateTime.Now, Request.Verb, Request.RequestUri, Request.HttpVersion);

            Response.HttpVersion = Request.HttpVersion;

            var responders = Server.Responders;

            IKayakResponder responding = null;

            Exception e = null;

            foreach (var responder in responders)
            {
                var willRespond = new AsyncOperation<bool>(callback => responder.WillRespond(this, callback));
                yield return willRespond;

                if (willRespond.Exception != null)
                {
                    e = new Exception("Exception during IKayakResponder.WillRespond", willRespond.Exception);
                    break;
                }

                if (willRespond.Result)
                {
                    responding = responder;
                    break;
                }
            }

            if (e == null)
            {
                if (responding == null)
                    Response.SetStatusToNotFound();
                else
                {
                    var respond = new AsyncOperation((exception) => responding.Respond(this, exception));
                    yield return respond;

                    if (respond.Exception != null)
                        e = new Exception("Exception during IKayakResponder.Respond", respond.Exception);
                }
            }

            if (e != null)
            {
                Response.Behavior = ResponseBehavior.SendResponse;
                Response.ClearOutput();
                Response.SetStatusToInternalServerError();
                Items[ContextExceptionKey] = e;
            }

            if (Response.Behavior != ResponseBehavior.Disconnect)
            {
                var defaultResponder = new AsyncOperation(exception => Server.DefaultResponder.Respond(this, exception));
                yield return defaultResponder;

                var complete = Response.Complete().Coroutine();
                yield return complete;
                if (CanLog)
                    Console.WriteLine("[{0}] {1} {2} {3}", DateTime.Now, Response.HttpVersion, Response.StatusCode, Response.ReasonPhrase);

                if (complete.Exception != null)
                    throw new Exception("Exception while completing response.", complete.Exception);
            }
        }
    }
}
