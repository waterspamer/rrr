using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServerSyncedComponentAttribute : Attribute
{
    public string ComponentId { get; }

    public ServerSyncedComponentAttribute(string componentId = null)
    {
        ComponentId = string.IsNullOrWhiteSpace(componentId) ? string.Empty : componentId.Trim();
    }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true)]
public sealed class ServerSyncedPropertyAttribute : Attribute
{
    public string PropertyName { get; }

    public ServerSyncedPropertyAttribute(string propertyName = null)
    {
        PropertyName = string.IsNullOrWhiteSpace(propertyName) ? string.Empty : propertyName.Trim();
    }
}

[Serializable]
public sealed class ServerSyncedComponentState
{
    private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

    public ServerSyncedComponentState(string componentId)
    {
        ComponentId = string.IsNullOrWhiteSpace(componentId) ? string.Empty : componentId.Trim();
    }

    public string ComponentId { get; }
    public IReadOnlyDictionary<string, object> Values => values;

    public void Set(string propertyName, object value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return;

        values[propertyName.Trim()] = ServerSyncedStateUtility.CloneValue(value);
    }

    public bool TryGetRawValue(string propertyName, out object value)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            value = null;
            return false;
        }

        return values.TryGetValue(propertyName.Trim(), out value);
    }

    public bool TryGetValue<T>(string propertyName, out T value)
    {
        if (TryGetRawValue(propertyName, out object raw) &&
            ServerSyncedStateUtility.TryConvertValue(raw, typeof(T), out object converted) &&
            converted is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public ServerSyncedComponentState DeepClone()
    {
        ServerSyncedComponentState clone = new ServerSyncedComponentState(ComponentId);
        foreach (KeyValuePair<string, object> pair in values)
            clone.values[pair.Key] = ServerSyncedStateUtility.CloneValue(pair.Value);
        return clone;
    }
}

public static class ServerSyncedStateUtility
{
    private sealed class MemberDescriptor
    {
        public string PropertyName;
        public Type ValueType;
        public Func<object, object> Getter;
        public Action<object, object> Setter;
    }

    private sealed class ComponentDescriptor
    {
        public string ComponentId;
        public List<MemberDescriptor> Members = new List<MemberDescriptor>();
    }

    private static readonly Dictionary<Type, ComponentDescriptor> DescriptorCache = new Dictionary<Type, ComponentDescriptor>();
    private static readonly object DescriptorLock = new object();

    public static bool IsServerSyncedComponent(Component component)
    {
        return component != null && GetDescriptor(component.GetType()) != null;
    }

    public static string ResolveComponentId(Component component)
    {
        ComponentDescriptor descriptor = component != null ? GetDescriptor(component.GetType()) : null;
        return descriptor != null ? descriptor.ComponentId : string.Empty;
    }

    public static ServerSyncedComponentState CaptureComponent(Component component)
    {
        if (component == null)
            return null;

        ComponentDescriptor descriptor = GetDescriptor(component.GetType());
        if (descriptor == null)
            return null;

        ServerSyncedComponentState state = new ServerSyncedComponentState(descriptor.ComponentId);
        for (int i = 0; i < descriptor.Members.Count; i++)
        {
            MemberDescriptor member = descriptor.Members[i];
            if (member.Getter == null)
                continue;

            state.Set(member.PropertyName, member.Getter(component));
        }

        return state;
    }

    public static bool ApplyComponent(Component component, ServerSyncedComponentState state)
    {
        if (component == null || state == null)
            return false;

        ComponentDescriptor descriptor = GetDescriptor(component.GetType());
        if (descriptor == null)
            return false;

        for (int i = 0; i < descriptor.Members.Count; i++)
        {
            MemberDescriptor member = descriptor.Members[i];
            if (member.Setter == null)
                continue;
            if (!state.TryGetRawValue(member.PropertyName, out object rawValue))
                continue;
            if (!TryConvertValue(rawValue, member.ValueType, out object converted))
                continue;

            member.Setter(component, converted);
        }

        return true;
    }

    public static object CloneValue(object value)
    {
        if (value == null)
            return null;

        Type type = value.GetType();
        if (type.IsArray)
        {
            Array source = (Array)value;
            Array clone = Array.CreateInstance(type.GetElementType(), source.Length);
            for (int i = 0; i < source.Length; i++)
                clone.SetValue(CloneValue(source.GetValue(i)), i);
            return clone;
        }

        if (type.IsValueType || value is string)
            return value;

        if (value is ICloneable cloneable)
            return cloneable.Clone();

        return value;
    }

    public static bool TryConvertValue(object value, Type targetType, out object converted)
    {
        if (targetType == null)
        {
            converted = value;
            return true;
        }

        if (value == null)
        {
            converted = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            return !targetType.IsValueType || converted != null;
        }

        Type valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
        {
            converted = CloneValue(value);
            return true;
        }

        if (targetType.IsArray && value is Array sourceArray)
        {
            Type elementType = targetType.GetElementType();
            Array clone = Array.CreateInstance(elementType, sourceArray.Length);
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (!TryConvertValue(sourceArray.GetValue(i), elementType, out object elementValue))
                {
                    converted = null;
                    return false;
                }

                clone.SetValue(elementValue, i);
            }

            converted = clone;
            return true;
        }

        if (targetType.IsEnum)
        {
            if (value is string stringValue)
            {
                converted = Enum.Parse(targetType, stringValue, true);
                return true;
            }

            if (value is IConvertible)
            {
                converted = Enum.ToObject(targetType, value);
                return true;
            }
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(targetType))
        {
            converted = Convert.ChangeType(value, targetType);
            return true;
        }

        converted = null;
        return false;
    }

    private static ComponentDescriptor GetDescriptor(Type componentType)
    {
        if (componentType == null)
            return null;

        lock (DescriptorLock)
        {
            if (DescriptorCache.TryGetValue(componentType, out ComponentDescriptor cached))
                return cached;

            ComponentDescriptor created = CreateDescriptor(componentType);
            DescriptorCache[componentType] = created;
            return created;
        }
    }

    private static ComponentDescriptor CreateDescriptor(Type componentType)
    {
        ServerSyncedComponentAttribute attribute = componentType.GetCustomAttribute<ServerSyncedComponentAttribute>(false);
        if (attribute == null)
            return null;

        ComponentDescriptor descriptor = new ComponentDescriptor
        {
            ComponentId = string.IsNullOrWhiteSpace(attribute.ComponentId) ? componentType.Name : attribute.ComponentId
        };

        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo[] properties = componentType.GetProperties(flags);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            ServerSyncedPropertyAttribute propertyAttribute = property.GetCustomAttribute<ServerSyncedPropertyAttribute>(true);
            if (propertyAttribute == null || property.GetIndexParameters().Length > 0)
                continue;

            MethodInfo getter = property.GetGetMethod(true);
            MethodInfo setter = property.GetSetMethod(true);
            descriptor.Members.Add(new MemberDescriptor
            {
                PropertyName = string.IsNullOrWhiteSpace(propertyAttribute.PropertyName) ? property.Name : propertyAttribute.PropertyName,
                ValueType = property.PropertyType,
                Getter = getter != null ? owner => getter.Invoke(owner, null) : null,
                Setter = setter != null ? (owner, value) => setter.Invoke(owner, new[] { value }) : null
            });
        }

        FieldInfo[] fields = componentType.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            ServerSyncedPropertyAttribute fieldAttribute = field.GetCustomAttribute<ServerSyncedPropertyAttribute>(true);
            if (fieldAttribute == null)
                continue;

            descriptor.Members.Add(new MemberDescriptor
            {
                PropertyName = string.IsNullOrWhiteSpace(fieldAttribute.PropertyName) ? field.Name : fieldAttribute.PropertyName,
                ValueType = field.FieldType,
                Getter = owner => field.GetValue(owner),
                Setter = field.IsInitOnly ? null : (owner, value) => field.SetValue(owner, value)
            });
        }

        descriptor.Members.Sort((left, right) => string.Compare(left.PropertyName, right.PropertyName, StringComparison.Ordinal));
        return descriptor;
    }
}
