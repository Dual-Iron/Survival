﻿using BepInEx;
using System;

namespace Survival
{
    [BepInPlugin("com.github.dual.survival", "Survival", "0.1.0")]
    public sealed class SurvivalPlugin : BaseUnityPlugin
    {
        public SurvivalRules? SurvivalRule { get; private set; }

        public void OnEnable()
        {
            try
            {
                SurvivalRule = new(Logger);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        public void OnDisable()
        {
            SurvivalRule = null;
        }
    }
}