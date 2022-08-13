using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ReAgent.State;

public sealed class CustomDynamicLinqCustomTypeProvider :
    AbstractDynamicLinqCustomTypeProvider,
    IDynamicLinkCustomTypeProvider,
    IDynamicLinqCustomTypeProvider
{
    private HashSet<Type> _cachedCustomTypes;
    private Dictionary<Type, List<MethodInfo>> _cachedExtensionMethods;

    public HashSet<Type> GetCustomTypes()
    {
        return _cachedCustomTypes ??= GetCustomTypesInternal();
    }

    public Dictionary<Type, List<MethodInfo>> GetExtensionMethods()
    {
        return _cachedExtensionMethods ??= GetExtensionMethodsInternal();
    }

    public Type ResolveType(string typeName)
    {
        return ResolveType(AppDomain.CurrentDomain.GetAssemblies(), typeName);
    }

    public Type ResolveTypeBySimpleName(string simpleTypeName)
    {
        return ResolveTypeBySimpleName(AppDomain.CurrentDomain.GetAssemblies(), simpleTypeName);
    }

    private HashSet<Type> GetCustomTypesInternal() => new(FindTypesMarkedWithDynamicLinqTypeAttribute(AppDomain.CurrentDomain.GetAssemblies()).Concat(typeof(CustomDynamicLinqCustomTypeProvider).Assembly.GetExportedTypes()));

    private Dictionary<Type, List<MethodInfo>> GetExtensionMethodsInternal()
    {
        var customTypes = GetCustomTypes();
        return customTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.IsDefined(typeof(ExtensionAttribute), false)))
            .GroupBy(x => x.GetParameters()[0].ParameterType)
            .ToDictionary(key => key.Key, methods => methods.ToList());
    }
}