using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    [HarmonyPatch]
    public static class MatchInfoGuideButtonPatch
    {
        private static bool Prepare() => AccessTools.TypeByName("MatchInfoHudButton") != null;

        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("MatchInfoHudButton"), "Update");

        public static bool Prefix(MonoBehaviour __instance)
        {
            __instance.gameObject.SetActive(false);
            return false;
        }
    }
}
