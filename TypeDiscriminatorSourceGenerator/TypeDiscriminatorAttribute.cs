using System;

namespace TLS.TypeDiscriminatorSourceGenerator
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class TypeDiscriminatorAttribute : Attribute
    {
    }
}
