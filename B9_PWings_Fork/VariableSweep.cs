using System;
using System.Reflection;
using UnityEngine;

namespace WingProcedural
{
    /// <summary>
    /// Reads the flap setting the rest of the vessel is already flying at, so variable-aspect
    /// wings follow the aircraft's real flaps when it has any. Under FAR that is the 0-3 vessel
    /// flap level; without FAR it is the stock control surfaces' Deploy toggle, which is on/off
    /// only and so reads as 0 or 3. Wings carry their own flap setting for craft that have no
    /// control surfaces to take it from - see WingProcedural's variable sweep region.
    /// </summary>
    public static class VariableSweep
    {
        private static Func<Vessel, int> farVesselFlapSetting;
        private static bool farChecked;

        /// <summary>
        /// Bind FerramAerospaceResearch.FARAPI.VesselFlapSetting(Vessel) once, from the assembly
        /// WingProcedural.CheckAssemblies has already matched. The plugin ships built without a FAR
        /// reference, hence reflection - but bound as a delegate, since this is read every frame
        /// and MethodInfo.Invoke would box the return and allocate an argument array each time.
        /// </summary>
        public static void InitFAR(Assembly far)
        {
            if (farChecked)
            {
                return;
            }

            farChecked = true;
            MethodInfo mi = far.GetType("FerramAerospaceResearch.FARAPI")
                              ?.GetMethod("VesselFlapSetting", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Vessel) }, null);
            if (mi != null && mi.ReturnType == typeof(int))
            {
                farVesselFlapSetting = (Func<Vessel, int>)Delegate.CreateDelegate(typeof(Func<Vessel, int>), mi);
            }
            else
            {
                Debug.Log("B9 PWings - FARAPI.VesselFlapSetting not found, variable sweep falls back to the stock deploy toggle");
            }
        }

        // Keyed by instance ID rather than the Vessel itself, so a stale cache entry cannot pin a
        // whole part tree alive across a scene load.
        private static int cachedVesselID;
        private static int cachedFrame = -1;
        private static int cachedLevel;

        /// <summary>
        /// Flap setting 0-3, or -1 when the vessel has no flap source of its own to read.
        /// Memoized per frame so a craft carrying dozens of wings only walks the part list once.
        /// </summary>
        public static int VesselFlapLevel(Vessel v)
        {
            if (v == null)
            {
                return -1;
            }

            int id = v.GetInstanceID();
            if (id == cachedVesselID && cachedFrame == Time.frameCount)
            {
                return cachedLevel;
            }

            cachedVesselID = id;
            cachedFrame = Time.frameCount;
            cachedLevel = Measure(v);
            return cachedLevel;
        }

        private static int Measure(Vessel v)
        {
            if (farVesselFlapSetting != null)
            {
                try
                {
                    // 0-3, or -1 when the craft has no flap-tagged surface at all
                    int level = farVesselFlapSetting(v);
                    return level < 0 ? -1 : Math.Min(level, 3);
                }
                catch (Exception e)
                {
                    Debug.Log("B9 PWings - FARAPI.VesselFlapSetting threw, falling back to the stock deploy toggle: " + e.Message);
                    farVesselFlapSetting = null;
                }
            }

            bool any = false;
            for (int i = 0; i < v.parts.Count; ++i)
            {
                ModuleControlSurface cs = v.parts[i].Modules.GetModule<ModuleControlSurface>();
                if (cs == null)
                {
                    continue;
                }

                any = true;
                if (cs.deploy)
                {
                    return 3;
                }
            }

            return any ? 0 : -1;
        }
    }
}
