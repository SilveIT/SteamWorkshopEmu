using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SteamWorkshopEmu
{
    //Utility class for shorter calls
    public static class R
    {
        public static SReflectionHelper.Types T => SteamWorkshopEmuPlugin.I.ReflectionHelper.T;
        public static SReflectionHelper.Properties P => SteamWorkshopEmuPlugin.I.ReflectionHelper.P;
        public static SReflectionHelper.Fields F => SteamWorkshopEmuPlugin.I.ReflectionHelper.F;
        public static SReflectionHelper.Methods M => SteamWorkshopEmuPlugin.I.ReflectionHelper.M;

        //Make sure you didn't get the object type .ctor()
        public static ConstructorInfo C(Type type, Type[] parameters = null, bool searchForStatic = false) =>
            AccessTools.Constructor(type, parameters, searchForStatic);

        public static ConstructorInfo C(Type type, Func<ConstructorInfo, bool> predicate) =>
            AccessTools.FirstConstructor(type, predicate);

        public static List<ConstructorInfo> Cs(Type type, bool? searchForStatic = null) =>
            AccessTools.GetDeclaredConstructors(type, searchForStatic);
    }
}