using System;

namespace Kayak
{
    public interface IKayakActivator
    {
        object CreateInstance(Type type);
        void AddInstances(object[] instances);
    }
}