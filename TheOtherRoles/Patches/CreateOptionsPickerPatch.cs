using AmongUs.GameOptions;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using static UnityEngine.UI.Button;

namespace TheOtherRoles.Patches {
    [HarmonyPatch(typeof(CreateOptionsPicker))]
    class CreateOptionsPickerPatch {
        private static List<SpriteRenderer> renderers = new();

        // Mode selection on this screen now happens via the repurposed "Game Mode" button
        // (CreateGameOptionsTORBehaviour.Awake, in Modules/CustomOptions.cs), which sets
        // TORMapOptions.gameMode directly and never calls SetGameMode. This Prefix used to
        // force TORMapOptions.gameMode back to Classic whenever vanilla called SetGameMode
        // with a base mode (Normal/HideNSeek) internally (e.g. during Refresh/server-switch
        // syncing), which would silently wipe out the player's TOR mode choice on this screen.
        // Left as a no-op passthrough so vanilla's own SetGameMode calls no longer interfere.
        [HarmonyPatch(typeof(CreateOptionsPicker), nameof(CreateOptionsPicker.SetGameMode))]
        public static bool Prefix(CreateOptionsPicker __instance, ref GameModes mode) {
            return true;
        }


        [HarmonyPatch(typeof(CreateOptionsPicker), nameof(CreateOptionsPicker.Refresh))]
        public static void Postfix(CreateOptionsPicker __instance) {
            if (SubmergedCompatibility.Loaded) return;

            if (TORMapOptions.gameMode == CustomGamemodes.Guesser) {
                __instance.GameModeText.text = ModTranslation.getString("torGuesser");
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.HideNSeek) {
                __instance.GameModeText.text = ModTranslation.getString("torHideNSeek");
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.FreePlay)
            {
                __instance.GameModeText.text = ModTranslation.getString("torFreePlay");
            }
            else if (TORMapOptions.gameMode == CustomGamemodes.Zombie)
            {
                __instance.GameModeText.text = ModTranslation.getString("torZombie");
            }
        }
    }

    [HarmonyPatch(typeof(GameModeMenu))]
    class GameModeMenuPatch {
        [HarmonyPatch(typeof(GameModeMenu), nameof(GameModeMenu.OnEnable))]
        public static bool Prefix(GameModeMenu __instance) {
            if (SubmergedCompatibility.Loaded) return true;

            uint gameMode = (uint)__instance.Parent.GetTargetOptions().GameMode;
            float num = ((float)Mathf.CeilToInt(4f / 10f) / 2f - 0.5f) * -2.5f;   // 4 for 4 buttons!
            __instance.controllerSelectable.Clear();
            int num2 = 0;
            __instance.ButtonPool.poolSize = 4;
            for (int i=0; i <= 4; i++) {
                    GameModes entry = (GameModes)i;
                if (entry != GameModes.None) {
                    ChatLanguageButton chatLanguageButton = __instance.ButtonPool.Get<ChatLanguageButton>();
                    chatLanguageButton.transform.localPosition = new Vector3(num + (float)(num2 / 10) * 2.5f, 2f - (float)(num2 % 10) * 0.5f, 0f);
                    if (i <= 2)
                        chatLanguageButton.Text.text = DestroyableSingleton<TranslationController>.Instance.GetString(GameModesHelpers.ModeToName[entry], new Il2CppReferenceArray<Il2CppSystem.Object>(0));
                    else {
                        chatLanguageButton.Text.text = i == 3 ? ModTranslation.getString("torGuesser") : ModTranslation.getString("torHideNSeek");
                        if (i == 5)
                            chatLanguageButton.Text.text = "TOR Fee Play";
                    }
                    chatLanguageButton.Button.OnClick.RemoveAllListeners();
                    chatLanguageButton.Button.OnClick.AddListener((System.Action)delegate {
                        __instance.ChooseOption(entry);
                    });

                    bool isCurrentMode = i <= 2 && TORMapOptions.gameMode == CustomGamemodes.Classic ? (long)entry == (long)((ulong)gameMode) : (i == 3 && TORMapOptions.gameMode == CustomGamemodes.Guesser || i == 4 && TORMapOptions.gameMode == CustomGamemodes.HideNSeek
                        || i == 5 && TORMapOptions.gameMode == CustomGamemodes.FreePlay);
                    chatLanguageButton.SetSelected(isCurrentMode);
                    __instance.controllerSelectable.Add(chatLanguageButton.Button);
                    if (isCurrentMode) {
                        __instance.defaultButtonSelected = chatLanguageButton.Button;
                    }
                    num2++;
                }
            }
            ControllerManager.Instance.OpenOverlayMenu(__instance.name, __instance.BackButton, __instance.defaultButtonSelected, __instance.controllerSelectable, false);
            return false;
        }
    }
}
