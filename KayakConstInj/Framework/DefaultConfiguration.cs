using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LitJson;
using System.Reflection;
using Kayak.Async;

namespace Kayak.Framework
{
    /// <summary>
    /// Used to specify an InvocationInfo to InvocationResponder. The InvocationInfo
    /// is used to construct a KayakInvocation instance.
    /// </summary>
    public class MappingEventArgs : EventArgs
    {
        public KayakContext Context { get; private set; }
        public InvocationInfo InvocationInfo { get; set; }

        internal MappingEventArgs(KayakContext context, InvocationInfo invocationInfo)
        {
            Context = context;
            InvocationInfo = invocationInfo;
        }
    }

    /// <summary>
    /// Raised by InvocationResponder before and after a KayakInvocation is invoked.
    /// </summary>
    public class InvocationEventArgs : EventArgs
    {
        public KayakInvocation Invocation { get; private set; }

        internal InvocationEventArgs(KayakInvocation invocation)
        {
            Invocation = invocation;
        }
    }

    public class InvokingEventArgs : InvocationEventArgs
    {
        public bool CancelInvocation { get; set; }

        internal InvokingEventArgs(KayakInvocation invocation) : base(invocation) { }
    }

    public class InvokedEventArgs : InvocationEventArgs
    {
        public Exception Exception { get; private set; }

        internal InvokedEventArgs(KayakInvocation invocation, Exception e)
            : base(invocation)
        {
            Exception = e;
        }
    }

    public sealed class DefaultConfiguration : IKayakResponder
    {
        static readonly string InvocationInfoContextKey = "InvocationInfo";
        private object[] _Instances;

        public event EventHandler<MappingEventArgs> Mapping;
        public event EventHandler<InvokingEventArgs> Invoking;
        public event EventHandler<InvokedEventArgs> Invoked;

        public MethodMap MethodMap { get; set; }
        HeaderDataBinder ParameterBinder { get; set; }
        public JsonDeserializer JsonDeserializer { get; set; }
        public JsonSerializer JsonSerializer { get; set; }

        public DefaultConfiguration(params Type[] types)
        {
            MethodMap = types.CreateMethodMap();
            BindParameters();
        }

        // TODO : Ferhat Burayi ben parcaladim. Bind PArameters olarak
        public DefaultConfiguration(params object[] instances)
        {
            _Instances = instances;
            Type[] types = instances.Select(x => x.GetType()).ToArray();
            MethodMap = types.CreateMethodMap();
            BindParameters();
        }

        private void BindParameters()
        {
            ParameterBinder = new HeaderDataBinder();

            JsonDeserializer = new JsonDeserializer();
            JsonDeserializer.Mapper.AddDefaultInputConversions();

            JsonSerializer = new JsonSerializer();
            JsonSerializer.Mapper.AddDefaultOutputConversions();
        }


        public void WillRespond(KayakContext context, Action<bool, Exception> callback)
        {
            try
            {
                
                InvocationInfo invocationInfo = new InvocationInfo();
                invocationInfo.Method = GetMethodForContext(context);
                invocationInfo = RaiseMapping(context, invocationInfo);

                bool willRespond = invocationInfo.Method != null;

                if (willRespond)
                    context.Items[InvocationInfoContextKey] = invocationInfo;

                callback(willRespond, null);
            }
            catch (Exception e)
            {
                callback(false, e);
            }
        }

        MethodInfo GetMethodForContext(KayakContext context)
        {
            bool invalidVerb = false;

            MethodInfo method = MethodMap.GetMethodForContext(context, out invalidVerb);

            if (invalidVerb)
                method = typeof(InvalidMethodResponse).GetMethod("Respond");

            return method;
        }

        public void Respond(KayakContext context, Action<Exception> callback)
        {
            try
            {
                var invocationInfo = (InvocationInfo)context.Items[InvocationInfoContextKey];

                var invocation = new KayakInvocation(context, invocationInfo);
                
                // TODO :Ferhat bunu da ben ekledim.
                invocation.Context.Activator.AddInstances(_Instances);
                
                bool cancel = RaiseInvoking(invocation);

                if (cancel)
                    callback(null);
                else
                {
                    AddBindersAndHandlers(invocation);
                    invocation.InvokeWithCallback(e =>
                    {
                        RaiseInvoked(invocation, e);
                        callback(e);
                    });
                }
            }
            catch (Exception e)
            {
                callback(e);
            }

        }

        void AddBindersAndHandlers(KayakInvocation invocation)
        {
            invocation.Binders.Add(ParameterBinder);

            if (RequestBodyAttribute.IsDefinedOnParameters(invocation.Info.Method))
                invocation.Binders.Add(JsonDeserializer);

            if (invocation.Info.Method.ReturnType != typeof(void))
                invocation.Handlers.Add(JsonSerializer);
        }

        #region event boilerplate

        InvocationInfo RaiseMapping(KayakContext context, InvocationInfo invocationInfo)
        {
            if (Mapping != null)
            {
                var mappingArgs = new MappingEventArgs(context, invocationInfo);
                Mapping(this, mappingArgs);
                invocationInfo = mappingArgs.InvocationInfo;
            }

            return invocationInfo;
        }

        bool RaiseInvoking(KayakInvocation invocation)
        {
            var result = false;
            if (Invoking != null)
            {
                var args = new InvokingEventArgs(invocation);
                Invoking(this, args);
                result = args.CancelInvocation;
            }
            return result;
        }

        void RaiseInvoked(KayakInvocation invocation, Exception e)
        {
            if (Invoked != null)
            {
                var args = new InvokedEventArgs(invocation, e);
                Invoked(this, args);
            }
        }

        #endregion
    }

    class InvalidMethodResponse
    {
        KayakInvocation context;

        public InvalidMethodResponse(KayakInvocation c)
        {
            context = c;
        }

        public void Respond()
        {
            context.Context.Response.StatusCode = 405;
            context.Context.Response.ReasonPhrase = "Invalid Method";
            context.Context.Response.Write("Invalid method: " + context.Context.Request.Verb);
        }
    }
}
