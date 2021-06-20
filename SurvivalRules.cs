using BepInEx.Logging;
using Menu;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RWCustom;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Survival
{
    public sealed class SurvivalRules
    {
        private RainWorld? rw;
        public RainWorld RW => rw ??= UnityEngine.Object.FindObjectOfType<RainWorld>();
        public RainWorldGame? RWGame => RW.processManager.currentMainLoop as RainWorldGame;

        public ManualLogSource Logger { get; }

        private int saveNum;

        internal SurvivalRules(ManualLogSource logger)
        {
            Logger = logger;

            new Hook(typeof(Options).GetMethod("get_SaveFileName"), (Func<Func<Options, string>, Options, string>)GetterSaveFileName)
                .Apply();
            new Hook(typeof(StoryGameSession).GetMethod("get_RedIsOutOfCycles"), (Func<Func<StoryGameSession, bool>, StoryGameSession, bool>)GetterRedIsOutOfCycles)
                .Apply();

            IL.Menu.SlugcatSelectMenu.StartGame += SlugcatSelectMenu_StartGame;
            IL.Menu.SlugcatSelectMenu.MineForSaveData += SlugcatSelectMenu_MineForSaveData;
            On.Menu.SlugcatSelectMenu.Update += SlugcatSelectMenu_Update;
            On.Menu.SlugcatSelectMenu.UpdateStartButtonText += SlugcatSelectMenu_UpdateStartButtonText;
            On.Menu.StoryGameStatisticsScreen.CommunicateWithUpcomingProcess += StoryGameStatisticsScreen_CommunicateWithUpcomingProcess;
            On.Menu.StoryGameStatisticsScreen.GetDataFromGame += StoryGameStatisticsScreen_GetDataFromGame;
            On.Menu.StoryGameStatisticsScreen.AddBkgIllustration += StoryGameStatisticsScreen_AddBkgIllustration;
            On.RainWorldGame.GameOver += RainWorldGame_GameOver;
            On.DeathPersistentSaveData.SaveToString += DeathPersistentSaveData_SaveToString;
            On.HUD.TextPrompt.Update += TextPrompt_Update;

            static string GetterSaveFileName(Func<Options, string> orig, Options self)
            {
                return orig(self) + "_survival";
            }

            static bool GetterRedIsOutOfCycles(Func<StoryGameSession, bool> orig, StoryGameSession self)
            {
                return !self.saveState.deathPersistentSaveData.reinforcedKarma;
            }
        }

        private void TextPrompt_Update(On.HUD.TextPrompt.orig_Update orig, HUD.TextPrompt self)
        {
            orig(self);
            if (!string.IsNullOrEmpty(self.label.text) && self.pausedWarningText)
            {
                if (self.hud.owner is Player player && player.abstractCreature.world.game.IsStorySession && player.abstractCreature.world.game.clock > 1200)
                    self.label.text = "Paused - Warning! Quitting after 30 seconds into a cycle resets your current karma" + (player.KarmaIsReinforced ? " and karma shield" : "");
                else
                    self.label.text = "Paused";
            }
        }

        private string DeathPersistentSaveData_SaveToString(On.DeathPersistentSaveData.orig_SaveToString orig, DeathPersistentSaveData self, bool saveAsIfPlayerDied, bool saveAsIfPlayerQuit)
        {
            if (saveAsIfPlayerQuit)
            {
                if (saveAsIfPlayerDied)
                {
                    var tempKarma = self.karma;
                    var tempReinf = self.reinforcedKarma;
                    self.karma = 0;
                    self.reinforcedKarma = false;
                    var ret = orig(self, false, false);
                    self.karma = tempKarma;
                    self.reinforcedKarma = tempReinf;
                    return ret;
                }
                return orig(self, false, false);
            }
            return orig(self, saveAsIfPlayerDied, saveAsIfPlayerQuit);
        }

        private void RainWorldGame_GameOver(On.RainWorldGame.orig_GameOver orig, RainWorldGame self, Creature.Grasp dependentOnGrasp)
        {
            if (self.session is StoryGameSession sess)
            {
                if (dependentOnGrasp != null)
                {
                    sess.PlaceKarmaFlowerOnDeathSpot();
                    self.manager.musicPlayer?.DeathEvent();
                    return;
                }
            }
            orig(self, dependentOnGrasp);
        }

        private void StoryGameStatisticsScreen_GetDataFromGame(On.Menu.StoryGameStatisticsScreen.orig_GetDataFromGame orig, StoryGameStatisticsScreen self, KarmaLadderScreen.SleepDeathScreenDataPackage package)
        {
            orig(self, package);
            saveNum = package.saveState.saveStateNumber;
        }

        private void StoryGameStatisticsScreen_AddBkgIllustration(On.Menu.StoryGameStatisticsScreen.orig_AddBkgIllustration orig, StoryGameStatisticsScreen self)
        {
            SlugcatSelectMenu.SaveGameData saveGameData = SlugcatSelectMenu.MineForSaveData(self.manager, saveNum);
            if (saveGameData != null && saveGameData.ascended)
            {
                self.scene = new InteractiveMenuScene(self, self.pages[0], MenuScene.SceneID.Red_Ascend);
                self.pages[0].subObjects.Add(self.scene);
            }
            else
            {
                self.scene = new InteractiveMenuScene(self, self.pages[0], MenuScene.SceneID.RedsDeathStatisticsBkg);
                self.pages[0].subObjects.Add(self.scene);
            }
        }

        private void StoryGameStatisticsScreen_CommunicateWithUpcomingProcess(On.Menu.StoryGameStatisticsScreen.orig_CommunicateWithUpcomingProcess orig, StoryGameStatisticsScreen self, MainLoopProcess nextProcess)
        {
            orig(self, null);
            if (nextProcess is SlugcatSelectMenu ssm)
            {
                for (int i = 0; i < ssm.slugcatColorOrder.Length; i++)
                {
                    if (ssm.slugcatColorOrder[i] == saveNum)
                    {
                        ssm.slugcatPageIndex = i;
                        break;
                    }
                }
                ssm.UpdateSelectedSlugcatInMiscProg();
            }
        }

        private static bool IsCurrentDead(SlugcatSelectMenu self)
        {
            return self.saveGameData[self.slugcatPageIndex]?.redsDeath ?? false;
        }

        private void SlugcatSelectMenu_UpdateStartButtonText(On.Menu.SlugcatSelectMenu.orig_UpdateStartButtonText orig, SlugcatSelectMenu self)
        {
            if (!self.restartChecked && IsCurrentDead(self))
            {
                self.startButton.menuLabel.text = self.Translate("STATISTICS");
            }
            else orig(self);
        }

        private void SlugcatSelectMenu_Update(On.Menu.SlugcatSelectMenu.orig_Update orig, Menu.SlugcatSelectMenu self)
        {
            var restartUpTemp = self.restartUp;

            orig(self);

            if (!self.restartAvailable)
            {
                if (IsCurrentDead(self) && !Input.GetKey("r") && (self.slugcatPageIndex != 2 || !self.redIsDead) && !self.saveGameData[self.slugcatPageIndex].ascended)
                {
                    self.restartUp = restartUpTemp;
                    self.restartUp = Custom.LerpAndTick(self.restartUp, 1f, 0.07f, 0.025f);
                    if (self.restartUp == 1f)
                    {
                        self.restartAvailable = true;
                    }
                    self.restartCheckbox.pos.y = Mathf.Lerp(-50f, 30f, self.restartUp);
                }
            }
        }

        private void SlugcatSelectMenu_StartGame(ILContext il)
        {
            ILCursor cursor = new(il);

            if (!cursor.TryGotoNext(i => i.MatchLdfld<SlugcatSelectMenu>("redIsDead")))
            {
                Logger.LogError("StartGame: Missing instruction 1");
                return;
            }

            Instruction brToStatistics = cursor.Next.Next.Next;

            if (!cursor.TryGotoPrev(i => i.MatchLdarg(1)))
            {
                Logger.LogError("StartGame: Missing instruction 2");
                return;
            }

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<SlugcatSelectMenu, bool>>(IsCurrentDead);
            cursor.Emit(OpCodes.Brtrue, brToStatistics);

            if (!cursor.TryGotoNext(i => i.MatchLdcI4(2)) || !cursor.TryGotoNext(i => i.MatchLdcI4(2)))
            {
                Logger.LogError("StartGame: Missing instruction 3|4");
                return;
            }

            cursor.Next.OpCode = OpCodes.Ldarg_1;
        }

        private void SlugcatSelectMenu_MineForSaveData(ILContext il)
        {
            ILCursor cursor = new(il);

            if (!cursor.TryGotoNext(i => i.MatchLdstr(">REDSDEATH")))
            {
                Logger.LogError("MineForSaveData: Missing instruction 1");
                return;
            }

            if (!cursor.TryGotoPrev(i => i.MatchBneUn(out _)))
            {
                Logger.LogError("MineForSaveData: Missing instruction 2");
                return;
            }

            // Make the branch jump to the next instruction (effectively a no-op)
            cursor.Next.Operand = cursor.Next.Next;
        }
    }
}
