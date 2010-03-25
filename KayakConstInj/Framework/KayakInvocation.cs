using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kayak.Async;

namespace Kayak.Framework
{
    /// <summary>
    /// Represents an invocation of a method on a target object. 
    /// The type of Target should be assignable to Method.DeclaringType.
    /// </summary>
    public struct InvocationInfo
    {
        public object Target { get; set; }
        public MethodInfo Method { get; set; }
    }

    /// <summary>
    /// Binds values to method parameters, possibly reading the request body, and invokes the callback
    /// when finished. The dictionary will be pre-populated with keys corresponding to each of the 
    /// method's parameters.
    /// </summary>
    public interface IParameterBinder
    {
        void BindParameters(KayakInvocation invocation, Dictionary<ParameterInfo, object> arguments, Action<Exception> callback);
    }

    /// <summary>
    /// Does something with the result of a method invocation, possibly writing the response body,
    /// and invokes the callback when finished.
    /// </summary>
    public interface IResultHandler
    {
        void HandleResult(KayakInvocation invocation, object result, Action<Exception> callback);
    }

    /// <summary>
    /// Encapsulates an invocation of a method.
    /// </summary>
    public sealed class KayakInvocation
    {
        /// <summary>
        /// The KayakContext in which the method is being invoked.
        /// </summary>
        public KayakContext Context { get; private set; }

        /// <summary>
        /// The method (and instance) being invoked.
        /// </summary>
        public InvocationInfo Info { get; private set; }

        /// <summary>
        /// The IArgumentBinder instances which are called before invocation.
        /// </summary>
        public List<IParameterBinder> Binders { get; private set; }

        /// <summary>
        /// The IResultHandler instance which are called after invocation.
        /// </summary>
        public List<IResultHandler> Handlers { get; private set; }

        public KayakInvocation(KayakContext context, InvocationInfo info)
        {
            Context = context;
            Info = info;
            Binders = new List<IParameterBinder>();
            Handlers = new List<IResultHandler>();
        }

        internal IEnumerable<IYieldable> Invoke()
        {
            var arguments = new Dictionary<ParameterInfo, object>();

            foreach (var pi in Info.Method.GetParameters())
                arguments[pi] = null;

            foreach (var binder in Binders)
                yield return new AsyncOperation(callback => binder.BindParameters(this, arguments, callback));

            var target = Info.Target;
            var method = Info.Method;

            if (target == null)
            {
                var constr = method.DeclaringType.GetConstructor(new Type[] { typeof(KayakInvocation) });
                if (constr != null)
                    target = constr.Invoke(new object[] { this });
                else
                {
                    // TODO : Ferhat Activator replaced with Context.Activator
                    // target = Activator.CreateInstance(method.DeclaringType);
                    target = Context.Activator.CreateInstance(method.DeclaringType);
                }
            }

            var service = target as KayakService;

            if (service != null)
                service.Invocation = this;

            var parameters = method.GetParameters().OrderBy(pi => pi.Position).ToArray();

            object[] argArray = new object[parameters.Length];

            for (int i = 0; i < argArray.Length; i++)
            {
                var param = parameters[i];

                if (arguments.ContainsKey(param))
                    argArray[i] = arguments[param];
            }

            object result = method.Invoke(target, argArray);

            if (method.ReturnType != typeof(void))
            {
                if (result is IEnumerable<IYieldable>)
                {
                    var asyncExecution = (result as IEnumerable<IYieldable>).Coroutine();
                    yield return asyncExecution;

                    // problematic! handlers might serialize whatever the result of the last
                    // async operation was, which is not what we want. probably need to
                    // introduce an IYieldable like AsyncReturnValue or something.
                    //result = asyncExecution.Result;
                }
                else
                    foreach (var handler in Handlers)
                        yield return new AsyncOperation(callback => handler.HandleResult(this, result, callback));
            }
        }
    }


    public static class InvocationExtensions
    {
        public static void InvokeWithCallback(this KayakInvocation invocation, Action<Exception> callback)
        {
            try
            {
                Scheduler.Default.Add(new ScheduledTask()
                {
                    Coroutine = invocation.Invoke().Coroutine(),
                    Callback = coroutine =>
                    {
                        if (coroutine.Exception != null)
                        {
                            //Console.WriteLine("Exception during KayakInvocation.Invoke");
                            Console.Out.WriteException(coroutine.Exception);
                        }

                        callback(coroutine.Exception);
                    }
                });
            }
            catch (Exception e)
            {
                callback(e);
            }
        }
    }
}
