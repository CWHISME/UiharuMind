using System.Reflection;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Extensions
{
    public static class ReflectionExt
    {
        /// <summary>
        /// 获取指定名字的字段值
        /// </summary>
        /// <param name="targetObject"></param>
        /// <param name="fieldName"></param>
        /// <param name="flags"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetFieldValue<T>(this object targetObject, string fieldName,
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            if (targetObject == null)
                return default(T);

            var objectType = targetObject.GetType();
            var field = objectType.GetFieldIncludingBaseClasses(fieldName, flags);
            if (field == null)
            {
                Log.Error($"(GetFieldValue)Invalid Field {fieldName} From:{objectType.Name} ");
                return default(T);
            }

            if (field.FieldType != typeof(T) && !field.FieldType.IsSubclassOf(typeof(T)))
            {
                Log.Error($"(GetFieldValue)Invalid Field {fieldName} Type:{field.FieldType} From:{typeof(T).Name} ");
                return default(T);
            }

            return (T)field.GetValue(targetObject);
        }

        /// <summary>
        /// 设置指定名字的字段值
        /// </summary>
        /// <param name="targetObject"></param>
        /// <param name="fieldName"></param>
        /// <param name="value"></param>
        /// <param name="flags"></param>
        /// <typeparam name="T"></typeparam>
        public static void SetFieldValue<T>(this object targetObject, string fieldName, T value,
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            if (targetObject == null)
                return;

            var objectType = targetObject.GetType();
            var targetType = typeof(T);
            var field = objectType.GetFieldIncludingBaseClasses(fieldName, flags);
            if (field == null)
            {
                Log.Error($"(SetFieldValue)Invalid Field {fieldName} From:{objectType.Name} ");
                return;
            }

            if (field.FieldType != targetType && !field.FieldType.IsSubclassOf(targetType))
            {
                Log.Error($"(SetFieldValue)Invalid Field {fieldName} Type:{field.FieldType} From:{targetType.Name} ");
                return;
            }

            field.SetValue(targetObject, value);
        }

        /// <summary>
        /// 反射获取属性
        /// </summary>
        /// <param name="targetObject"></param>
        /// <param name="propertyName"></param>
        /// <param name="bindingFlags"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T GetPropertyValue<T>(this object targetObject, string propertyName,
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            if (targetObject == null)
                return default(T);

            var objectType = targetObject.GetType();
            var property = objectType.GetPropertyIncludingBaseClasses(propertyName, bindingFlags);
            if (property == null)
            {
                Log.Error($"(GetPropertyValue)Invalid Property {propertyName} From:{objectType.Name} ");
                return default(T);
            }

            if (property.PropertyType != typeof(T) && !property.PropertyType.IsSubclassOf(typeof(T)))
            {
                Log.Error(
                    $"(GetPropertyValue)Invalid Property {propertyName} Type:{property.PropertyType} From:{typeof(T).Name} ");
                return default(T);
            }

            return (T)property.GetValue(targetObject);
        }

        /// <summary>
        /// 反射设置属性
        /// </summary>
        /// <param name="targetObject"></param>
        /// <param name="propertyName"></param>
        /// <param name="value"></param>
        /// <param name="bindingFlags"></param>
        /// <typeparam name="T"></typeparam>
        public static void SetPropertyValue<T>(this object targetObject, string propertyName, T value,
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        {
            if (targetObject == null)
                return;

            var objectType = targetObject.GetType();
            var targetType = typeof(T);
            var property = objectType.GetPropertyIncludingBaseClasses(propertyName, bindingFlags);
            if (property == null)
            {
                Log.Error($"(SetPropertyValue)Invalid Property {propertyName} From:{objectType.Name} ");
                return;
            }

            if (property.PropertyType != targetType && !property.PropertyType.IsSubclassOf(targetType))
            {
                Log.Error(
                    $"(SetPropertyValue)Invalid Property {propertyName} Type:{property.PropertyType} From:{targetType.Name} ");
                return;
            }

            property.SetValue(targetObject, value);
        }

        /// <summary>
        /// 获取某个属性，含父类
        /// </summary>
        /// <param name="type"></param>
        /// <param name="propertyName"></param>
        /// <param name="bindingFlags"></param>
        /// <returns></returns>
        public static PropertyInfo GetPropertyIncludingBaseClasses(this Type type, string propertyName,
            BindingFlags bindingFlags)
        {
            PropertyInfo property = null;

            // 遍历当前类型及其所有基类
            while (type != null && property == null)
            {
                property = type.GetProperty(propertyName, bindingFlags);
                type = type.BaseType;
            }

            return property;
        }

        /// <summary>
        /// 获取某个字段，含父类
        /// </summary>
        /// <param name="type"></param>
        /// <param name="fieldName"></param>
        /// <param name="bindingFlags"></param>
        /// <returns></returns>
        public static FieldInfo GetFieldIncludingBaseClasses(this Type type, string fieldName,
            BindingFlags bindingFlags)
        {
            FieldInfo field = null;

            // 遍历当前类型及其所有基类
            while (type != null && field == null)
            {
                field = type.GetField(fieldName, bindingFlags);
                type = type.BaseType;
            }

            return field;
        }

        /// <summary>
        /// 获取所有实现了某个接口的类型
        /// </summary>
        /// <param name="assembly"></param>
        /// <param name="interfaceName"></param>
        /// <returns></returns>
        public static Type[] GetTypesOfInterface(this Assembly assembly, string interfaceName)
        {
            return assembly.GetTypes().Where(t => t.GetInterface(interfaceName) != null).ToArray();
        }
        
        public static Type[] GetTypesOfSubclass(this Assembly assembly, Type baseType)
        {
            return assembly.GetTypes().Where(t => t.IsSubclassOf(baseType)).ToArray();
        }
    }
}