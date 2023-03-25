using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;

// ReSharper disable UnusedMember.Global

namespace SteamWorkshopEmu
{
    /// <summary>
    /// <para>This class provides you 4 different attributes for requesting MemberInfo from an unknown assembly</para>
    /// <para>You can load MethodInfos all at once and then use 4 helper classes to access them in multiple ways</para>
    /// </summary>
    public class SReflectionHelper
    {
        //Storage for loaded MethodInfos
        public Dictionary<string, Type> LoadedTypes { get; }
        public Dictionary<Type, Dictionary<string, PropertyInfo>> LoadedProperties { get; }
        public Dictionary<Type, Dictionary<string, FieldInfo>> LoadedFields { get; }
        public Dictionary<Type, Dictionary<string, Dictionary<MethodArguments, MethodInfo>>> LoadedMethods { get; }

        //Helper instances for loaded info
        public Types T { get; }
        public Properties P { get; }
        public Fields F { get; }
        public Methods M { get; }

        public bool Verbose { get; }

        /// <param name="verbose">Console output</param>
        public SReflectionHelper(bool verbose = false)
        {
            Verbose = verbose;
            LoadedTypes = new Dictionary<string, Type>();
            LoadedProperties = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
            LoadedFields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
            LoadedMethods = new Dictionary<Type, Dictionary<string, Dictionary<MethodArguments, MethodInfo>>>();
            T = new Types(this);
            P = new Properties(this);
            F = new Fields(this);
            M = new Methods(this);
        }

        /// <summary>
        /// Gathers all attributes with requirements from selected assembly, then tries to resolve all requirements
        /// </summary>
        /// <param name="assembly">Assembly containing requirement attributes</param>
        /// <returns>List of errors</returns>
        public List<string> LoadRequiredMemberInfoForAssembly(Assembly assembly)
        {
            //Welp, object type is not IEquatable, let's use string :)
            var rootRequirement = new RequiredMemberInfo<string>(MemberInfoTypes.Root, null, null, null);

            foreach (var typeWithRequirements in AccessTools.GetTypesFromAssembly(assembly))
            {
                var attrs = GetRequirementsFromType(typeWithRequirements).ToList();
                foreach (var attr in attrs)
                {
                    var typeReqs = AppendRequirements(MemberInfoTypes.Type, rootRequirement, attr.Type, attr.RequiredBy);

                    switch (attr)
                    {
                        case PropertyRequired attrAsProp:
                            AppendRequirements(MemberInfoTypes.Property, typeReqs, attrAsProp.Property, attrAsProp.RequiredBy);
                            break;
                        case FieldRequired attrAsField:
                            AppendRequirements(MemberInfoTypes.Field, typeReqs, attrAsField.Field, attrAsField.RequiredBy);
                            break;
                        case MethodRequired attrAsMethod:
                            var container =
                                AppendRequirements(MemberInfoTypes.MethodContainer, typeReqs, attrAsMethod.Method, attrAsMethod.RequiredBy);
                            AppendRequirements(MemberInfoTypes.Method, container, attrAsMethod.MethodArgTypes, attrAsMethod.RequiredBy);
                            break;
                    }
                }
            }
            var t = ResolveRequirements(rootRequirement.RequiredMemberInfos);
            return t;
        }

        /// <summary>
        /// Core recursive method for resolving requirement tree
        /// </summary>
        /// <param name="requirements"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private List<string> ResolveRequirements(List<RequiredMemberInfoBase> requirements)
        {
            var failed = new List<string>();
            foreach (var requirement in requirements)
            {
                var result = true;
                switch (requirement.MemberType)
                {
                    case MemberInfoTypes.Root:
                        throw new NotSupportedException();
                    case MemberInfoTypes.MethodContainer:
                        break;
                    case MemberInfoTypes.Type:
                        {
                            if (!(requirement is RequiredMemberInfo<string> req))
                                throw new InvalidOperationException("Cannot cast type requirement to RequiredMemberInfo<string>");
                            var t = AccessTools.TypeByName(req.MemberKey);
                            if (t != null)
                                LoadedTypes.Add(req.MemberKey, t);
                            else
                                result = false;
                            if (Verbose)
                                Console.WriteLine($"[{(result ? "+" : "-")}] Resolving {requirement.MemberType} with name {req.MemberKey}");
                            break;
                        }
                    case MemberInfoTypes.Property:
                        {
                            if (!(requirement is RequiredMemberInfo<string> reqOfString))
                                throw new InvalidOperationException("Cannot cast property requirement to RequiredMemberInfo<string>");
                            if (!(requirement.Parent is RequiredMemberInfo<string> typeReqOfString))
                                throw new InvalidOperationException("Cannot cast type requirement to RequiredMemberInfo<string>");
                            var t = T[typeReqOfString.MemberKey];
                            var p = AccessTools.Property(t, reqOfString.MemberKey);
                            if (p != null)
                            {
                                InitDictionary(LoadedProperties, t);
                                LoadedProperties[t][reqOfString.MemberKey] = p;
                            }
                            else
                                result = false;
                            if (Verbose)
                                Console.WriteLine($"[{(result ? "+" : "-")}] Resolving {reqOfString.MemberType} with name {reqOfString.MemberKey}");
                            break;
                        }
                    case MemberInfoTypes.Field:
                        {
                            if (!(requirement is RequiredMemberInfo<string> reqOfString))
                                throw new InvalidOperationException("Cannot cast field requirement to RequiredMemberInfo<string>");
                            if (!(requirement.Parent is RequiredMemberInfo<string> typeReqOfString))
                                throw new InvalidOperationException("Cannot cast type requirement to RequiredMemberInfo<string>");
                            var t = T[typeReqOfString.MemberKey];
                            var p = AccessTools.Field(t, reqOfString.MemberKey);
                            if (p != null)
                            {
                                InitDictionary(LoadedFields, t);
                                LoadedFields[t][reqOfString.MemberKey] = p;
                            }
                            else
                                result = false;
                            if (Verbose)
                                Console.WriteLine($"[{(result ? "+" : "-")}] Resolving {reqOfString.MemberType} with name {reqOfString.MemberKey}");
                            break;
                        }
                    case MemberInfoTypes.Method:
                        {
                            if (!(requirement is RequiredMemberInfo<MethodArguments> reqOfMethodArguments))
                                throw new InvalidOperationException("Cannot cast requirement to RequiredMemberInfo<MethodArguments>");
                            if (!(requirement.Parent is RequiredMemberInfo<string> methodReqOfString))
                                throw new InvalidOperationException("Cannot cast method requirement to RequiredMemberInfo<string>");
                            if (!(requirement.Parent.Parent is RequiredMemberInfo<string> typeReqOfString))
                                throw new InvalidOperationException("Cannot cast type requirement to RequiredMemberInfo<string>");
                            var methodName = methodReqOfString.MemberKey;
                            var args = reqOfMethodArguments.MemberKey;
                            var t = T[typeReqOfString.MemberKey];
                            MethodInfo p;
                            if (args.Arguments == null)
                            {
                                if (args.ArgumentCount != -1)
                                    p = AccessTools.FirstMethod(t,
                                        info => info.Name == methodName &&
                                                info.GetParameters().Length == args.ArgumentCount);
                                else
                                    p = AccessTools.FirstMethod(t, info => info.Name == methodName);
                            }
                            else
                                p = AccessTools.Method(t, methodName, args.Arguments);

                            if (p != null)
                            {
                                InitDictionary(LoadedMethods, t);
                                InitDictionary(LoadedMethods[t], methodName);
                                LoadedMethods[t][methodName][args] = p;
                            }
                            else
                                result = false;
                            if (Verbose)
                                Console.WriteLine($"[{(result ? "+" : "-")}] " +
                                              $"Resolving {reqOfMethodArguments.MemberType} with name {methodReqOfString.MemberKey} " +
                                              $"and arguments: {reqOfMethodArguments.MemberKey}");
                            break;
                        }
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (!result)
                {
                    failed.AddRange(requirement.RequiredByList);
                    continue;
                }

                if (requirement.RequiredMemberInfos != null && requirement.RequiredMemberInfos.Count != 0)
                    failed.AddRange(ResolveRequirements(requirement.RequiredMemberInfos));
            }

            return failed;
        }

        /// <summary>
        /// Helps to initialize recursive dictionaries
        /// </summary>
        private static void InitDictionary<TRoot, TKey, TValue>(IDictionary<TRoot, Dictionary<TKey, TValue>> loadedMembers, TRoot root)
            where TRoot : class
            where TKey : class
            where TValue : class
        {
            if (loadedMembers.ContainsKey(root))
                return;
            loadedMembers.Add(root, new Dictionary<TKey, TValue>());
        }

        /// <summary>
        /// Aggregates all the requirements into a single tree
        /// </summary>
        /// <typeparam name="T">MemberKey type of current branch</typeparam>
        /// <param name="targetType">Required MemberInfo type</param>
        /// <param name="requirements">Tree branch</param>
        /// <param name="requiredKey">Branch key</param>
        /// <param name="requiredBy">Key requirement</param>
        /// <returns></returns>
        private static RequiredMemberInfo<T> AppendRequirements<T>(MemberInfoTypes targetType, RequiredMemberInfoBase requirements, T requiredKey, string requiredBy)
            where T : IEquatable<T>
        {
            if (requirements.RequiredMemberInfos == null) //TODO: optimize checks mb
                requirements.RequiredMemberInfos = new List<RequiredMemberInfoBase>();
            var reqsOfType = requirements.RequiredMemberInfos.OfType<RequiredMemberInfo<T>>();
            var targetRequirements = reqsOfType.SingleOrDefault(o => o.MemberKey.Equals(requiredKey));
            if (targetRequirements != null)
                targetRequirements.AddRequirement(requiredBy);
            else
            {
                targetRequirements = new RequiredMemberInfo<T>(targetType, requiredKey, requiredBy, requirements);
                requirements.RequiredMemberInfos.Add(targetRequirements);
            }

            return targetRequirements;
        }

        /// <summary>
        /// Gathers all requirement attributes from selected type
        /// </summary>
        /// <param name="type">Selected type</param>
        /// <returns>Collcetion of attributes</returns>
        private static IEnumerable<TypeRequired> GetRequirementsFromType(Type type) =>
            type.GetCustomAttributes(true).OfType<TypeRequired>();

        /// <summary>
        /// Finds and attribute with selected type and attribute index,
        /// the attribute index also takes into account the attributes from which the selected attribute is nested.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="typeWithRequirements">Type with attributes</param>
        /// <param name="attributeIndex">Attribute index (derives from base attributes)</param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static T GetRequirementAtIndex<T>(Type typeWithRequirements, int attributeIndex = 0)
            where T : TypeRequired, IEquatable<T>
        {
            if (attributeIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(attributeIndex),
                    "Attribute index can't be negative");
            var attrs = GetRequirementsFromType(typeWithRequirements).OfType<T>().ToList();
            if (attributeIndex >= attrs.Count)
                throw new ArgumentOutOfRangeException(nameof(attributeIndex),
                    "This type doesn't contain this many appropriate attributes");
            var attr = attrs[attributeIndex];
            return attr;
        }

        /// <summary>
        /// Access helper class for loaded types
        /// </summary>
        public class Types : ReflectionAccess
        {
            public Type this[string typeName] => Reflection.LoadedTypes[typeName];

            /// <summary>
            /// Finds all TypeRequired attributes on selected type,
            /// uses attribute at index attributeIndex (counts ALL requirement attributes) to find target info.
            /// </summary>
            /// <param name="typeWithRequirements">Selected type</param>
            /// <param name="attributeIndex">TypeRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public Type this[Type typeWithRequirements, int attributeIndex = 0]
            {
                get
                {
                    var req = GetRequirementAtIndex<TypeRequired>(typeWithRequirements, attributeIndex);
                    return Reflection.LoadedTypes[req.Type];
                }
            }

            /// <summary>
            /// Finds all TypeRequired attributes on caller type,
            /// uses attribute at index attributeIndex (counts ALL requirement attributes) to find target info.
            /// 3x times slower than passing type.
            /// </summary>
            /// <param name="attributeIndex">TypeRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public Type this[int attributeIndex]
            {
                get
                {
                    var req = GetRequirementAtIndex<TypeRequired>(new StackFrame(1).GetMethod().DeclaringType, attributeIndex);
                    return Reflection.LoadedTypes[req.Type];
                }
            }

            public Types(SReflectionHelper helper) : base(helper) { }
        }

        /// <summary>
        /// Access helper class for loaded properties
        /// </summary>
        public class Properties : ReflectionAccess
        {
            public PropertyInfo this[Type type, string propertyName] => Reflection.LoadedProperties[type][propertyName];
            public PropertyInfo this[string typeName, string propertyName] => Reflection.LoadedProperties[Reflection.LoadedTypes[typeName]][propertyName];

            /// <summary>
            /// Finds all PropertyRequired attributes on selected type,
            /// uses attribute at index attributeIndex (counts only PropertyRequired) to find target info.
            /// </summary>
            /// <param name="typeWithRequirements">Selected type</param>
            /// <param name="attributeIndex">PropertyRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public PropertyInfo this[Type typeWithRequirements, int attributeIndex = 0]
            {
                get
                {
                    var req = GetRequirementAtIndex<PropertyRequired>(typeWithRequirements, attributeIndex);
                    return Reflection.LoadedProperties[Reflection.LoadedTypes[req.Type]][req.Property];
                }
            }

            /// <summary>
            /// Finds all PropertyRequired attributes on caller type,
            /// uses attribute at index attributeIndex (counts only PropertyRequired) to find target info.
            /// 3x times slower than passing type.
            /// </summary>
            /// <param name="attributeIndex">PropertyRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public PropertyInfo this[int attributeIndex]
            {
                get
                {
                    var req = GetRequirementAtIndex<PropertyRequired>(new StackFrame(1).GetMethod().DeclaringType, attributeIndex);
                    return Reflection.LoadedProperties[Reflection.LoadedTypes[req.Type]][req.Property];
                }
            }

            public Properties(SReflectionHelper helper) : base(helper) { }
        }

        /// <summary>
        /// Access helper class for loaded fields
        /// </summary>
        public class Fields : ReflectionAccess
        {
            public FieldInfo this[Type type, string fieldName] => Reflection.LoadedFields[type][fieldName];
            public FieldInfo this[string typeName, string fieldName] => Reflection.LoadedFields[Reflection.LoadedTypes[typeName]][fieldName];

            /// <summary>
            /// Finds all FieldRequired attributes on selected type,
            /// uses attribute at index attributeIndex (counts only FieldRequired) to find target info.
            /// </summary>
            /// <param name="typeWithRequirements">Selected type</param>
            /// <param name="attributeIndex">FieldRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public FieldInfo this[Type typeWithRequirements, int attributeIndex = 0]
            {
                get
                {
                    var req = GetRequirementAtIndex<FieldRequired>(typeWithRequirements, attributeIndex);
                    return Reflection.LoadedFields[Reflection.LoadedTypes[req.Type]][req.Field];
                }
            }

            /// <summary>
            /// Finds all FieldRequired attributes on caller type,
            /// uses attribute at index attributeIndex (counts only FieldRequired) to find target info.
            /// 3x times slower than passing type.
            /// </summary>
            /// <param name="attributeIndex">FieldRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public FieldInfo this[int attributeIndex]
            {
                get
                {
                    var req = GetRequirementAtIndex<FieldRequired>(new StackFrame(1).GetMethod().DeclaringType, attributeIndex);
                    return Reflection.LoadedFields[Reflection.LoadedTypes[req.Type]][req.Field];
                }
            }

            public Fields(SReflectionHelper helper) : base(helper) { }
        }

        /// <summary>
        /// Access helper class for loaded methods
        /// </summary>
        public class Methods : ReflectionAccess
        {
            public MethodInfo this[Type type, string methodName, params Type[] argTypes] => GetMethodInfo(type, methodName, argTypes);
            public MethodInfo this[string typeName, string methodName, params Type[] argTypes] => GetMethodInfo(typeName, methodName, argTypes);
            public MethodInfo this[Type type, string methodName] => GetMethodInfo(type, methodName);
            public MethodInfo this[string typeName, string methodName] => GetMethodInfo(typeName, methodName);

            /// <summary>
            /// Finds all MethodRequired attributes on selected type,
            /// uses attribute at index attributeIndex (counts only MethodRequired) to find target info.
            /// </summary>
            /// <param name="typeWithRequirements">Selected type</param>
            /// <param name="attributeIndex">MethodRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public MethodInfo this[Type typeWithRequirements, int attributeIndex = 0]
            {
                get
                {
                    var req = GetRequirementAtIndex<MethodRequired>(typeWithRequirements, attributeIndex);
                    return GetMethodInfo(req);
                }
            }

            /// <summary>
            /// Finds all MethodRequired attributes on caller type,
            /// uses attribute at index attributeIndex (counts only MethodRequired) to find target info.
            /// 3x times slower than passing type.
            /// </summary>
            /// <param name="attributeIndex">MethodRequired attribute index</param>
            /// <returns></returns>
            /// <exception cref="ArgumentOutOfRangeException"></exception>
            public MethodInfo this[int attributeIndex]
            {
                get
                {
                    var req = GetRequirementAtIndex<MethodRequired>(new StackFrame(1).GetMethod().DeclaringType, attributeIndex);
                    return GetMethodInfo(req);
                }
            }

            private MethodInfo GetMethodInfo(MethodRequired req) =>
                GetMethodInfo(req.Type, req.Method, req.MethodArgTypes);

            private MethodInfo GetMethodInfo(string typeName, string methodName) =>
                GetMethodInfo(Reflection.LoadedTypes[typeName], methodName);

            private MethodInfo GetMethodInfo(Type type, string methodName) =>
                Reflection.LoadedMethods[type][methodName].Values.First();

            private MethodInfo GetMethodInfo(string typeName, string methodName, MethodArguments args) =>
                GetMethodInfo(Reflection.LoadedTypes[typeName], methodName, args);

            private MethodInfo GetMethodInfo(Type type, string methodName, MethodArguments args) =>
                Reflection.LoadedMethods[type][methodName][args];

            public Methods(SReflectionHelper helper) : base(helper) { }
        }

        public abstract class ReflectionAccess
        {
            internal SReflectionHelper Reflection;

            protected ReflectionAccess(SReflectionHelper helper) => Reflection = helper;
        }
    }

    /// <summary>
    /// Requirement tree branch member
    /// </summary>
    public class RequiredMemberInfoBase
    {
        public List<string> RequiredByList { get; }
        public MemberInfoTypes MemberType { get; }

        public RequiredMemberInfoBase Parent { get; }

        public List<RequiredMemberInfoBase> RequiredMemberInfos;

        public RequiredMemberInfoBase(MemberInfoTypes memberType, string requiredBy, RequiredMemberInfoBase parent)
        {
            MemberType = memberType;
            RequiredByList = new List<string> { requiredBy };
            Parent = parent;
        }

        public void AddRequirement(string name) => RequiredByList.Add(name);
    }

    /// <summary>
    /// Generic requirement tree branch member with a MemberKey property of T type
    /// </summary>
    /// <typeparam name="T">Typically string or MethodArguments type</typeparam>
    public class RequiredMemberInfo<T> : RequiredMemberInfoBase
        where T : IEquatable<T>
    {
        public T MemberKey { get; }

        public RequiredMemberInfo(MemberInfoTypes memberType, T memberKey, string requiredBy, RequiredMemberInfoBase parent)
            : base(memberType, requiredBy, parent) =>
            MemberKey = memberKey;
    }

    /// <summary>
    /// Tree segments
    /// </summary>
    public enum MemberInfoTypes
    {
        Root,
        MethodContainer,
        Type,
        Property,
        Field,
        Method
    }

    /// <summary>
    /// <para>Requirement attribute for type.</para>
    /// It's recommended to chain these attributes before all the others,
    /// so you won't mess up attribute indexes while using ReflectionAccess for types
    /// as other Required attributes not only derive from this class but also share their attribute index with this class
    /// <para>So if you set TypeRequired after PropertyRequired attribute, then TypeRequired attribute will be at T[1].
    /// At T[0] will be the type requested in the PropertyRequired attribute.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class TypeRequired : Attribute, IEquatable<TypeRequired>
    {
        public string Type { get; }
        public string RequiredBy { get; }

        /// <param name="name">Type name with namespaces</param>
        /// <param name="requiredBy">The reason why this member is necessary</param>
        public TypeRequired(string name, string requiredBy)
        {
            Type = name;
            RequiredBy = requiredBy;
        }

        public bool Equals(TypeRequired other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && Type == other.Type && RequiredBy == other.RequiredBy;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((TypeRequired)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (Type != null ? Type.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (RequiredBy != null ? RequiredBy.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Requirement attribute for type and its property at the same time.
    /// </summary>
    public class PropertyRequired : TypeRequired, IEquatable<PropertyRequired>
    {
        public string Property { get; }

        /// <param name="type">Type name with namespaces</param>
        /// <param name="propertyName">Selected property</param>
        /// <param name="requiredBy">The reason why this member is necessary</param>
        public PropertyRequired(string type, string propertyName, string requiredBy) : base(type, requiredBy) => Property = propertyName;

        public bool Equals(PropertyRequired other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && Property == other.Property;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((PropertyRequired)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ (Property != null ? Property.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// Requirement attribute for type and its field at the same time.
    /// </summary>
    public class FieldRequired : TypeRequired, IEquatable<FieldRequired>
    {
        public string Field { get; }

        /// <param name="type">Type name with namespaces</param>
        /// <param name="fieldName">Selected field</param>
        /// <param name="requiredBy">The reason why this member is necessary</param>
        public FieldRequired(string type, string fieldName, string requiredBy) : base(type, requiredBy) => Field = fieldName;

        public bool Equals(FieldRequired other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && Field == other.Field;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((FieldRequired)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ (Field != null ? Field.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// Requirement attribute for type and its method at the same time. Supports method overloads.
    /// </summary>
    public class MethodRequired : TypeRequired, IEquatable<MethodRequired>
    {
        public string Method { get; }
        public MethodArguments MethodArgTypes { get; }

        /// <summary>
        /// Please don't forget about setting argCount when you target an overload with 0 arguments
        /// </summary>
        /// <param name="type">Type name with namespaces</param>
        /// <param name="methodName">Selected method</param>
        /// <param name="requiredBy">The reason why this member is necessary</param>
        /// <param name="argCount">How many arguments the selected method overload has</param>
        public MethodRequired(string type, string methodName, string requiredBy, int argCount = -1) : base(type, requiredBy)
        {
            Method = methodName;
            MethodArgTypes = argCount;
        }


        /// <param name="type">Type name with namespaces</param>
        /// <param name="methodName">Selected method</param>
        /// <param name="requiredBy">The reason why this member is necessary</param>
        /// <param name="argTypes">Types of the selected method overload</param>
        public MethodRequired(string type, string methodName, string requiredBy, params Type[] argTypes) : base(type, requiredBy)
        {
            Method = methodName;
            MethodArgTypes = argTypes;
        }

        public bool Equals(MethodRequired other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return base.Equals(other) && Method == other.Method && Equals(MethodArgTypes, other.MethodArgTypes);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((MethodRequired)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (Method != null ? Method.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (MethodArgTypes != null ? MethodArgTypes.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Type array container
    /// </summary>
    public class MethodArguments : IEquatable<MethodArguments>
    {
        public ComparableArray<Type> Arguments { get; }
        public int ArgumentCount { get; }

        public static implicit operator MethodArguments(Type[] argTypes) => new MethodArguments(argTypes);
        public static implicit operator MethodArguments(int argCount) => new MethodArguments(argCount);

        public MethodArguments(Type[] argTypes)
        {
            Arguments = argTypes;
            ArgumentCount = argTypes.Length;
        }

        public MethodArguments(int argCount) => ArgumentCount = argCount;

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Arguments != null ? Arguments.GetHashCode() : 0) * 397) ^ ArgumentCount;
            }
        }

        public override string ToString()
        {
            var res = "Count = " + (ArgumentCount == -1 ? "unknown" : ArgumentCount.ToString());
            if (Arguments == null || ArgumentCount == 0) return res;
            res += "; Types: ";
            for (var i = 0; i < Arguments.Count; i++)
            {
                if (i != Arguments.Count - 1)
                    res += Arguments[i] + ", ";
                else
                    res += Arguments[i];

            }
            return res;
        }

        public bool Equals(MethodArguments other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Arguments, other.Arguments) && ArgumentCount == other.ArgumentCount;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((MethodArguments)obj);
        }
    }

    /// <summary>
    /// An array container with GetHashCode and Equals methods overridden to compare its array elements
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ComparableArray<T> : IEquatable<T[]>, ICollection
    {
        public static implicit operator ComparableArray<T>(T[] elements) => new ComparableArray<T>(elements);
        public static implicit operator T[](ComparableArray<T> comparableArray) => comparableArray.Array;
        public T this[int index] => Array[index];
        public ComparableArray(T[] array) => Array = array;

        public T[] Array { get; }
        public int Count => Array.Length;
        public object SyncRoot => Array.SyncRoot;
        public bool IsSynchronized => Array.IsSynchronized;

        public IEnumerator GetEnumerator() => Array.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                foreach (T element in Array)
                    hash = hash * 31 + element.GetHashCode();
                return hash;
            }
        }

        public bool Equals(T[] other)
        {
            if (Array == other)
                return true;
            if (Array == null || other == null)
                return false;
            if (Array.Length != other.Length)
                return false;
            for (var i = 0; i < Array.Length; i++)
                if (!Array[i].Equals(other[i]))
                    return false;

            return true;
        }
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ComparableArray<T>)obj);
        }

        public void CopyTo(Array array, int index) => Array.CopyTo(array, index);
    }
}