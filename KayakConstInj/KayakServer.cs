using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Kayak.Async;

namespace Kayak
{
    /// <summary>
    /// A light, fast, multi-threaded, asynchronous web server. BOOM. SKEET.
    /// </summary>
    public sealed class KayakServer
    {
        Socket listener;
        public bool running, stopping;
        int numConnections;
        object userContext;
        public bool CanLog { get; set; }
        List<IKayakResponder> responders;
        private const int RECEIVE_TIMEOUT = 5000;
        private const int SEND_TIMEOUT = 5000;


        /// <summary>
        /// The IPEndPoint the on which the server is listening.
        /// </summary>
        public IPEndPoint EndPoint { get { return (IPEndPoint)listener.LocalEndPoint; } }

        /// <summary>
        /// The objects configured to respond to requests made to the server.
        /// </summary>
        public List<IKayakResponder> Responders 
        { 
            get { lock (this) return new List<IKayakResponder>(responders); }
            set { lock (this) responders = value; }
        }

        internal IKayakResponder DefaultResponder { get; private set; }

        public event EventHandler Starting, Started, Stopping, Stopped;

        public KayakServer() : this(null) {
        }

        public KayakServer(object userContext)
        {
            this.userContext = userContext;
            responders = new List<IKayakResponder>();
            DefaultResponder = new DefaultResponder();
        }

        /// <summary>
        /// Starts the server on the default IP address on port 8080.
        /// </summary>
        public void Start()
        {
            Start(new IPEndPoint(IPAddress.Any, 8080));
        }

        /// <summary>
        /// Starts the server, listening on the given IPEndPoint.
        /// </summary>
        public void Start(IPEndPoint listenOn)
        {
            if (running) throw new Exception("already running");

            RaiseStarting();

            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            listener.Bind(listenOn);
            // TODO : Changed : RecevieTimout ferhat
            //listener.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.ReceiveTimeout, 10000);
            //listener.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.SendTimeout, 10000);
            listener.Listen(1000);

            running = true;

            RaiseStarted();

            Scheduler.Default.Add(new ScheduledTask()
            {
                Coroutine = AcceptConnections().Coroutine(),
                Callback = coroutine =>
                {
                    if (!stopping)
                    {
                        //Console.WriteLine("AcceptConnection terminated unexpectedly, something must have broke.");
                        if (coroutine.Exception != null)
                            Console.Out.WriteException(coroutine.Exception);
                    }
                }
            });

            Scheduler.Default.Start();
        }

        /// <summary>
        /// Stops the server, waiting for any pending HTTP transactions to finish.
        /// </summary>
        public void Stop()
        {
            if (!running) throw new Exception("not running");
            if (stopping) throw new Exception("already stopping");
            
            stopping = true;
            RaiseStopping();

            // check if there are no connections
            int nc = 0;
            lock (this)
                nc = numConnections;
            if (nc == 0)
                Shutdown();
        }

        void Shutdown()
        {
            Scheduler.Default.Stop();
            listener.Close();
            running = stopping = false;
            RaiseStopped();
        }

        IEnumerable<IYieldable> AcceptConnections()
        {
            while (!stopping)
            {
                var accept = listener.AcceptAsync();
                yield return accept;

                var start = DateTime.Now;

                if (accept.Exception != null)
                {
                    //Console.WriteLine("Exception while accepting connection!");
                    Console.Out.WriteException(accept.Exception);
                    continue;
                }

                lock (this)
                    numConnections++;

                //Console.WriteLine("Accepted a connection.");
                Socket socket = accept.Result; // socket is closed by context if no exception occurs
                if (socket == null)
                    yield break;
                var context = new KayakContext(this, socket);
                // TODO : Ferhat buraya loglama isini pasliyorum
                context.CanLog = false;

                Scheduler.Default.Add(new ScheduledTask()
                {
                    Coroutine = context.HandleConnection().Coroutine(),
                    Callback = coroutine =>
                    {
                        if (coroutine.Exception != null)
                        {
                            //Console.WriteLine("Exception during HandleConnection");
                            Console.Out.WriteException(coroutine.Exception);
                        }
                        //Console.Write("closing socket...");
                        socket.Close();
                        socket = null;
                        if (CanLog)
                            Console.WriteLine("{0}ms", (DateTime.Now - start).TotalMilliseconds);
                        //GC.Collect();
                        //Console.WriteLine("done.");
                        int nc = 0;
                        lock (this)
                            nc = --numConnections;
                        if (stopping && nc == 0)
                            Shutdown();
                    }
                });
            }
        }

        #region event boilerplate

        void RaiseStarting()
        {
            if (Starting != null)
                Starting(this, EventArgs.Empty);
        }

        void RaiseStarted()
        {
            if (Started != null)
                Started(this, EventArgs.Empty);
        }

        void RaiseStopping()
        {
            if (Stopping != null)
                Stopping(this, EventArgs.Empty);
        }

        void RaiseStopped()
        {
            if (Stopped != null)
                Stopped(this, EventArgs.Empty);
        }

        #endregion
    }

    public static class AsyncSocketExtensions
    {
        public static AsyncOperation<Socket> AcceptAsync(this Socket s)
        {
            return new AsyncOperation<Socket>(s.BeginAccept, s.EndAccept);
        }
    }
}
