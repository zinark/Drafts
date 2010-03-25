using System;
using System.Collections.Generic;

namespace Kayak
{
    internal class KayakActivator : IKayakActivator
    {
        private readonly IDictionary<Type,object> _Container = new Dictionary<Type, object>();

        public object CreateInstance(Type type)
        {
            if (_Container[type] != null)
                return _Container[type];

            return Activator.CreateInstance(type);
        }

        public void AddInstances(object[] instances)
        {
            foreach (var instance in instances)
                _Container[instance.GetType()] = instance;
        }

        public void AddInstance (object instance)
        {
            _Container.Add(instance.GetType(), instance);
        }
    }
}