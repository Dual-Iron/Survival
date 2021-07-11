using BepInEx;
using System;

namespace Survival
{
    [BepInPlugin("com.github.dual.survival", "Survival", "1.0.0")]
    public sealed class SurvivalPlugin : BaseUnityPlugin
    {
        public SurvivalMod? SurvivalRule { get; private set; }

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
