using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP.Localization;
using UnityEngine.Internal;
using UnityEngine.Scripting;


namespace WingProcedural
{
    public struct MathD // as we only need the clamp function so MathD.cs can be discard.
    {
        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
            {
                value = min;
            }
            else if (value.CompareTo(max) > 0)
            {
                value = max;
            }

            return value;
        }
    }
    public class WingProcedural : PartModule, IPartCostModifier, IPartSizeModifier, IPartMassModifier
    {
        // Some handy bools
        [KSPField]
        public bool isCtrlSrf = false;

        [KSPField]
        public bool isWingAsCtrlSrf = false;

        [KSPField]
        public bool isPanel = false;

        [KSPField(isPersistant = true)]
        public bool isAttached = false;

        public bool isMirrored = false;

        [KSPField(isPersistant = true)]
        public bool isSetToDefaultValues = false;

        #region Debug

        private struct DebugMessage
        {
            public string message;
            public string interval;

            public DebugMessage(string m, string i)
            {
                message = m;
                interval = i;
            }
        }

        private DateTime debugTime;
        private DateTime debugTimeLast;
        private readonly List<DebugMessage> debugMessageList = new List<DebugMessage>();

        private void DebugTimerUpdate()
        {
            debugTime = DateTime.UtcNow;
        }

        private void DebugLogWithID(string method, string message)
        {
            debugTime = DateTime.UtcNow;
            string m = "WP | ID: " + part.gameObject.GetInstanceID() + " | " + method + " | " + message;
            string i = (debugTime - debugTimeLast).TotalMilliseconds + " ms.";
            if (debugMessageList.Count <= 150)
            {
                debugMessageList.Add(new DebugMessage(m, i));
            }

            debugTimeLast = DateTime.UtcNow;
            Debug.Log(m);
        }

        #endregion Debug

        #region Mesh properties

        [System.Serializable]
        public class MeshReference
        {
            public Vector3[] vp;
            public Vector3[] nm;
            public Vector2[] uv;
        }

        public MeshFilter meshFilterWingSection;
        public MeshFilter meshFilterWingSurface;
        public readonly List<MeshFilter> meshFiltersWingEdgeTrailing = new List<MeshFilter>();
        public readonly List<MeshFilter> meshFiltersWingEdgeLeading = new List<MeshFilter>();

        public MeshFilter meshFilterCtrlFrame;
        public MeshFilter meshFilterCtrlSurface;
        public readonly List<MeshFilter> meshFiltersCtrlEdge = new List<MeshFilter>();

        public static MeshReference meshReferenceWingSection;
        public static MeshReference meshReferenceWingSurface;
        public static readonly List<MeshReference> meshReferencesWingEdge = new List<MeshReference>();

        public static MeshReference meshReferenceCtrlFrame;
        public static MeshReference meshReferenceCtrlSurface;
        public static readonly List<MeshReference> meshReferencesCtrlEdge = new List<MeshReference>();

        private static readonly int meshTypeCountEdgeWing = 4;
        private static readonly int meshTypeCountEdgeCtrl = 3;

        #endregion Mesh properties

        #region Shared properties / Limits and increments

        private Vector2 GetLimitsFromType(Vector4 set)
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logLimits)
            {
                DebugLogWithID("GetLimitsFromType", "Using set: " + set);
            }

            return isCtrlSrf ? new Vector2(set.z, set.w) : new Vector2(set.x, set.y);
        }

        private float GetIncrementFromType(float incrementWing, float incrementCtrl)
        {
            return isCtrlSrf ? incrementCtrl : incrementWing;
        }

        private static Vector4 sharedBaseLengthLimits = new Vector4(0.0f, 40f, 0.0f, 20f);
        private static Vector2 sharedBaseThicknessLimits = new Vector2(0.01f, 4f);
        private static Vector4 sharedBaseWidthRootLimits = new Vector4(0.01f, 40f, 0.01f, 2f);
        private static Vector4 sharedBaseWidthTipLimits = new Vector4(0.0f, 40f, 0.0f, 2f);

        private static Vector4 sharedBaseOffsetLimits = new Vector4(-10f, 10f, -1.5f, 1.5f);
        private static Vector4 sharedEdgeTypeLimits = new Vector4(1f, 4f, 1f, 3f);
        private static Vector4 sharedEdgeWidthLimits = new Vector4(0f, 6f, 0f, 6f);
        private static Vector2 sharedMaterialLimits = new Vector2(0f, 4f);
        private static Vector2 sharedColorLimits = new Vector2(0f, 1f);
        private static Vector2 positiveinf = new Vector2(0.0f, float.PositiveInfinity);
        private static Vector2 nolimit = new Vector2(float.NegativeInfinity, float.PositiveInfinity);
        private static Vector2 sharedArmorLimits = new Vector2(0f, 1000f);

        private static readonly float sharedIncrementColor = 0.01f;
        private static readonly float sharedIncrementColorLarge = 0.10f;
        private static readonly float sharedIncrementMain = 0.05f;
        private static readonly float sharedIncrementSmall = 0.005f;
        private static readonly float sharedIncrementInt = 1f;

        #endregion Shared properties / Limits and increments

        #region Shared properties / Base

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Base")]
        public static bool sharedFieldGroupBaseStatic = true;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Length", guiFormat = "S4")]
        public float sharedBaseLength = 4f;

        public float sharedBaseLengthCached = 4f;
        public static Vector4 sharedBaseLengthDefaults = new Vector4(4f, 1f, 4f, 1f);
        public int sharedBaseLengthInt = 0;
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Width (root)", guiFormat = "S4")]
        public float sharedBaseWidthRoot = 4f;

        public float sharedBaseWidthRootCached = 4f;
        public static Vector4 sharedBaseWidthRootDefaults = new Vector4(4f, 0.5f, 4f, 0.5f);

        //public int sharedBaseWidthRInt = 0;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Width (tip)", guiFormat = "S4")]
        public float sharedBaseWidthTip = 4f;

        public float sharedBaseWidthTipCached = 4f;
        public static Vector4 sharedBaseWidthTipDefaults = new Vector4(4f, 0.5f, 4f, 0.5f);
        public int sharedBaseWidthTInt = 0;
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Offset (root)", guiFormat = "S4")]
        public float sharedBaseOffsetRoot = 0f;

        public float sharedBaseOffsetRootCached = 0f;
        public static Vector4 sharedBaseOffsetRootDefaults = new Vector4(0f, 0f, 0f, 0f);
        public int sharedBaseOffsetRInt = 0;
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Offset (tip)", guiFormat = "S4")]
        public float sharedBaseOffsetTip = 0f;

        public float sharedBaseOffsetTipCached = 0f;
        public static Vector4 sharedBaseOffsetTipDefaults = new Vector4(0f, 0f, 0f, 0f);
        public int sharedBaseOffsetTInt = 0;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Thickness (root)", guiFormat = "F3")]
        public float sharedBaseThicknessRoot = 0.24f;

        public float sharedBaseThicknessRootCached = 0.24f;
        public static Vector4 sharedBaseThicknessRootDefaults = new Vector4(0.24f, 0.24f, 0.24f, 0.24f);

        //public int sharedBaseThicknessRInt = 0;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Thickness (tip)", guiFormat = "F3")]
        public float sharedBaseThicknessTip = 0.24f;

        public float sharedBaseThicknessTipCached = 0.24f;
        public static Vector4 sharedBaseThicknessTipDefaults = new Vector4(0.24f, 0.24f, 0.24f, 0.24f);

        //public int sharedBaseThicknessTInt = 0;

        #endregion Shared properties / Base

        #region Shared properties / Edge / Leading

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Lead. edge")]
        public static bool sharedFieldGroupEdgeLeadingStatic = false;

        private static readonly string[] sharedFieldGroupEdgeLeadingArray = new string[] { "sharedEdgeTypeLeading", "sharedEdgeWidthLeadingRoot", "sharedEdgeWidthLeadingTip" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Shape", guiFormat = "F3")]
        public float sharedEdgeTypeLeading = 2f;

        public float sharedEdgeTypeLeadingCached = 2f;
        public static Vector4 sharedEdgeTypeLeadingDefaults = new Vector4(2f, 1f, 2f, 1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Width (root)", guiFormat = "F3")]
        public float sharedEdgeWidthLeadingRoot = 0.24f;

        public float sharedEdgeWidthLeadingRootCached = 0.24f;
        public static Vector4 sharedEdgeWidthLeadingRootDefaults = new Vector4(0.24f, 0.24f, 0.24f, 0.24f);
        public int sharedEdgeWidthLRInt = 0;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Width (tip)", guiFormat = "F3")]
        public float sharedEdgeWidthLeadingTip = 0.24f;

        public float sharedEdgeWidthLeadingTipCached = 0.24f;
        public static Vector4 sharedEdgeWidthLeadingTipDefaults = new Vector4(0.24f, 0.24f, 0.24f, 0.24f);
        public int sharedEdgeWidthLTInt = 0;

        #endregion Shared properties / Edge / Leading

        #region Shared properties / Edge / Trailing

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Trail. edge")]
        public static bool sharedFieldGroupEdgeTrailingStatic = false;

        private static readonly string[] sharedFieldGroupEdgeTrailingArray = new string[] { "sharedEdgeTypeTrailing", "sharedEdgeWidthTrailingRoot", "sharedEdgeWidthTrailingTip" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Shape", guiFormat = "F3")]
        public float sharedEdgeTypeTrailing = 3f;

        public float sharedEdgeTypeTrailingCached = 3f;
        public static Vector4 sharedEdgeTypeTrailingDefaults = new Vector4(3f, 2f, 3f, 2f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Width (root)", guiFormat = "F3")]
        public float sharedEdgeWidthTrailingRoot = 0.48f;

        public float sharedEdgeWidthTrailingRootCached = 0.48f;
        public static Vector4 sharedEdgeWidthTrailingRootDefaults = new Vector4(0.48f, 0.48f, 0.48f, 0.48f);
        public int sharedEdgeWidthTRInt = 0;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Width (tip)", guiFormat = "F3")]
        public float sharedEdgeWidthTrailingTip = 0.48f;

        public float sharedEdgeWidthTrailingTipCached = 0.48f;
        public static Vector4 sharedEdgeWidthTrailingTipDefaults = new Vector4(0.48f, 0.48f, 0.48f, 0.48f);
        public int sharedEdgeWidthTTInt = 0;
        #endregion Shared properties / Edge / Trailing

        #region Shared properties / Surface / Top

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Material A")]
        public static bool sharedFieldGroupColorSTStatic = false;

        private static readonly string[] sharedFieldGroupColorSTArray = new string[] { "sharedMaterialST", "sharedColorSTOpacity", "sharedColorSTHue", "sharedColorSTSaturation", "sharedColorSTBrightness" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Material", guiFormat = "F3")]
        public float sharedMaterialST = 1f;

        public float sharedMaterialSTCached = 1f;
        public static Vector4 sharedMaterialSTDefaults = new Vector4(1f, 1f, 1f, 1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Opacity", guiFormat = "F3")]
        public float sharedColorSTOpacity = 0f;

        public float sharedColorSTOpacityCached = 0f;
        public static Vector4 sharedColorSTOpacityDefaults = new Vector4(0f, 0f, 0f, 0f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (H)", guiFormat = "F3")]
        public float sharedColorSTHue = 0.10f;

        public float sharedColorSTHueCached = 0.10f;
        public static Vector4 sharedColorSTHueDefaults = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (S)", guiFormat = "F3")]
        public float sharedColorSTSaturation = 0.75f;

        public float sharedColorSTSaturationCached = 0.75f;
        public static Vector4 sharedColorSTSaturationDefaults = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (B)", guiFormat = "F3")]
        public float sharedColorSTBrightness = 0.6f;

        public float sharedColorSTBrightnessCached = 0.6f;
        public static Vector4 sharedColorSTBrightnessDefaults = new Vector4(0.6f, 0.6f, 0.6f, 0.6f);

        #endregion Shared properties / Surface / Top

        #region Shared properties / Surface / bottom

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Material B")]
        public static bool sharedFieldGroupColorSBStatic = false;

        private static readonly string[] sharedFieldGroupColorSBArray = new string[] { "sharedMaterialSB", "sharedColorSBOpacity", "sharedColorSBHue", "sharedColorSBSaturation", "sharedColorSBBrightness" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Material", guiFormat = "F3")]
        public float sharedMaterialSB = 4f;

        public float sharedMaterialSBCached = 4f;
        public static Vector4 sharedMaterialSBDefaults = new Vector4(4f, 4f, 4f, 4f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Opacity", guiFormat = "F3")]
        public float sharedColorSBOpacity = 0f;

        public float sharedColorSBOpacityCached = 0f;
        public static Vector4 sharedColorSBOpacityDefaults = new Vector4(0f, 0f, 0f, 0f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (H)", guiFormat = "F3")]
        public float sharedColorSBHue = 0.10f;

        public float sharedColorSBHueCached = 0.10f;
        public static Vector4 sharedColorSBHueDefaults = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (S)", guiFormat = "F3")]
        public float sharedColorSBSaturation = 0.75f;

        public float sharedColorSBSaturationCached = 0.75f;
        public static Vector4 sharedColorSBSaturationDefaults = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (B)", guiFormat = "F3")]
        public float sharedColorSBBrightness = 0.6f;

        public float sharedColorSBBrightnessCached = 0.6f;
        public static Vector4 sharedColorSBBrightnessDefaults = new Vector4(0.6f, 0.6f, 0.6f, 0.6f);

        #endregion Shared properties / Surface / bottom

        #region Shared properties / Surface / trailing edge

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Material T")]
        public static bool sharedFieldGroupColorETStatic = false;

        private static readonly string[] sharedFieldGroupColorETArray = new string[] { "sharedMaterialET", "sharedColorETOpacity", "sharedColorETHue", "sharedColorETSaturation", "sharedColorETBrightness" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Material", guiFormat = "F3")]
        public float sharedMaterialET = 4f;

        public float sharedMaterialETCached = 4f;
        public static Vector4 sharedMaterialETDefaults = new Vector4(4f, 4f, 4f, 4f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Opacity", guiFormat = "F3")]
        public float sharedColorETOpacity = 0f;

        public float sharedColorETOpacityCached = 0f;
        public static Vector4 sharedColorETOpacityDefaults = new Vector4(0f, 0f, 0f, 0f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (H)", guiFormat = "F3")]
        public float sharedColorETHue = 0.10f;

        public float sharedColorETHueCached = 0.10f;
        public static Vector4 sharedColorETHueDefaults = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (S)", guiFormat = "F3")]
        public float sharedColorETSaturation = 0.75f;

        public float sharedColorETSaturationCached = 0.75f;
        public static Vector4 sharedColorETSaturationDefaults = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (B)", guiFormat = "F3")]
        public float sharedColorETBrightness = 0.6f;

        public float sharedColorETBrightnessCached = 0.6f;
        public static Vector4 sharedColorETBrightnessDefaults = new Vector4(0.6f, 0.6f, 0.6f, 0.6f);

        #endregion Shared properties / Surface / trailing edge

        #region Shared properties / Surface / leading edge

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "| Material L")]
        public static bool sharedFieldGroupColorELStatic = false;

        private static readonly string[] sharedFieldGroupColorELArray = new string[] { "sharedMaterialEL", "sharedColorELOpacity", "sharedColorELHue", "sharedColorELSaturation", "sharedColorELBrightness" };

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Material", guiFormat = "F3")]
        public float sharedMaterialEL = 4f;

        public float sharedMaterialELCached = 4f;
        public static Vector4 sharedMaterialELDefaults = new Vector4(4f, 4f, 4f, 4f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Opacity", guiFormat = "F3")]
        public float sharedColorELOpacity = 0f;

        public float sharedColorELOpacityCached = 0f;
        public static Vector4 sharedColorELOpacityDefaults = new Vector4(0f, 0f, 0f, 0f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (H)", guiFormat = "F3")]
        public float sharedColorELHue = 0.10f;

        public float sharedColorELHueCached = 0.10f;
        public static Vector4 sharedColorELHueDefaults = new Vector4(0.1f, 0.1f, 0.1f, 0.1f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (S)", guiFormat = "F3")]
        public float sharedColorELSaturation = 0.75f;

        public float sharedColorELSaturationCached = 0.75f;
        public static Vector4 sharedColorELSaturationDefaults = new Vector4(0.75f, 0.75f, 0.75f, 0.75f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Color (B)", guiFormat = "F3")]
        public float sharedColorELBrightness = 0.6f;

        public float sharedColorELBrightnessCached = 0.6f;
        public static Vector4 sharedColorELBrightnessDefaults = new Vector4(0.6f, 0.6f, 0.6f, 0.6f);

        #endregion Shared properties / Surface / leading edge

        #region Shared properties / Misc + Angles
        //Angles
        private static Vector2 sharedSweptAngleLimits = new Vector2(1f, 180f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Swept angle(front)", guiFormat = "F3")]
        public float sharedSweptAngleFront = 90f;
        public float sharedSweptAngleFrontCached = 90f;
        public static Vector4 sharedSweptAngleFrontCachedDefaults = new Vector4(90f, 90f, 90f, 90f);

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Swept angle(back)", guiFormat = "F3")]
        public float sharedSweptAngleBack = 90f;
        public float sharedSweptAngleBackCached = 90f;
        public static Vector4 sharedSweptAngleBackDefaults = new Vector4(90f, 90f, 90f, 90f);

        //Armor
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Swept angle(back)", guiFormat = "F3")]
        public float sharedArmorRatio = 0;
        public float sharedArmorRatioCached = 0;
        //Prefs
        public static bool sharedFieldPrefStatic = true;
        public static bool sharedPropAnglePref = false;
        public static bool sharedPropLockPref = false;
        public static bool sharedPropLock2Pref = false;
        public static bool sharedPropLock3Pref = false;
        public static bool sharedPropEdgePref = false;
        public static bool sharedPropEThickPref = false;
        public static bool sharedArmorPref = false;
        private static readonly float sharedIncrementAngle = 1f;
        private static readonly float sharedIncrementAngleLarge = 5f;


        #endregion

        #region Default values

        // Vector4 (defaultWing, defaultCtrl, defaultWingBackup, defaultCtrlBackup)

        private void ReplaceDefaults()
        {
            ReplaceDefault(ref sharedBaseLengthDefaults, sharedBaseLength);
            ReplaceDefault(ref sharedBaseWidthRootDefaults, sharedBaseWidthRoot);
            ReplaceDefault(ref sharedBaseWidthTipDefaults, sharedBaseWidthTip);
            ReplaceDefault(ref sharedBaseOffsetRootDefaults, sharedBaseOffsetRoot);
            ReplaceDefault(ref sharedBaseOffsetTipDefaults, sharedBaseOffsetTip);
            ReplaceDefault(ref sharedBaseThicknessRootDefaults, sharedBaseThicknessRoot);
            ReplaceDefault(ref sharedBaseThicknessTipDefaults, sharedBaseThicknessTip);

            ReplaceDefault(ref sharedEdgeTypeLeadingDefaults, sharedEdgeTypeLeading);
            ReplaceDefault(ref sharedEdgeWidthLeadingRootDefaults, sharedEdgeWidthLeadingRoot);
            ReplaceDefault(ref sharedEdgeWidthLeadingTipDefaults, sharedEdgeWidthLeadingTip);

            ReplaceDefault(ref sharedEdgeTypeTrailingDefaults, sharedEdgeTypeTrailing);
            ReplaceDefault(ref sharedEdgeWidthTrailingRootDefaults, sharedEdgeWidthTrailingRoot);
            ReplaceDefault(ref sharedEdgeWidthTrailingTipDefaults, sharedEdgeWidthTrailingTip);

            ReplaceDefault(ref sharedMaterialSTDefaults, sharedMaterialST);
            ReplaceDefault(ref sharedColorSTOpacityDefaults, sharedColorSTOpacity);
            ReplaceDefault(ref sharedColorSTHueDefaults, sharedColorSTHue);
            ReplaceDefault(ref sharedColorSTSaturationDefaults, sharedColorSTSaturation);
            ReplaceDefault(ref sharedColorSTBrightnessDefaults, sharedColorSTBrightness);

            ReplaceDefault(ref sharedMaterialSBDefaults, sharedMaterialSB);
            ReplaceDefault(ref sharedColorSBOpacityDefaults, sharedColorSBOpacity);
            ReplaceDefault(ref sharedColorSBHueDefaults, sharedColorSBHue);
            ReplaceDefault(ref sharedColorSBSaturationDefaults, sharedColorSBSaturation);
            ReplaceDefault(ref sharedColorSBBrightnessDefaults, sharedColorSBBrightness);

            ReplaceDefault(ref sharedMaterialETDefaults, sharedMaterialET);
            ReplaceDefault(ref sharedColorETOpacityDefaults, sharedColorETOpacity);
            ReplaceDefault(ref sharedColorETHueDefaults, sharedColorETHue);
            ReplaceDefault(ref sharedColorETSaturationDefaults, sharedColorETSaturation);
            ReplaceDefault(ref sharedColorETBrightnessDefaults, sharedColorETBrightness);

            ReplaceDefault(ref sharedMaterialELDefaults, sharedMaterialEL);
            ReplaceDefault(ref sharedColorELOpacityDefaults, sharedColorELOpacity);
            ReplaceDefault(ref sharedColorELHueDefaults, sharedColorELHue);
            ReplaceDefault(ref sharedColorELSaturationDefaults, sharedColorELSaturation);
            ReplaceDefault(ref sharedColorELBrightnessDefaults, sharedColorELBrightness);
        }

        private void ReplaceDefault(ref Vector4 set, float value)
        {
            set = !isCtrlSrf ? new Vector4(value, set.w, set.z, set.w) : new Vector4(set.z, value, set.z, set.w);
        }

        private void RestoreDefaults()
        {
            RestoreDefault(ref sharedBaseLengthDefaults);
            RestoreDefault(ref sharedBaseWidthRootDefaults);
            RestoreDefault(ref sharedBaseWidthTipDefaults);
            RestoreDefault(ref sharedBaseOffsetRootDefaults);
            RestoreDefault(ref sharedBaseOffsetTipDefaults);
            RestoreDefault(ref sharedBaseThicknessRootDefaults);
            RestoreDefault(ref sharedBaseThicknessTipDefaults);

            RestoreDefault(ref sharedEdgeTypeLeadingDefaults);
            RestoreDefault(ref sharedEdgeWidthLeadingRootDefaults);
            RestoreDefault(ref sharedEdgeWidthLeadingTipDefaults);

            RestoreDefault(ref sharedEdgeTypeTrailingDefaults);
            RestoreDefault(ref sharedEdgeWidthTrailingRootDefaults);
            RestoreDefault(ref sharedEdgeWidthTrailingTipDefaults);

            RestoreDefault(ref sharedMaterialSTDefaults);
            RestoreDefault(ref sharedColorSTOpacityDefaults);
            RestoreDefault(ref sharedColorSTHueDefaults);
            RestoreDefault(ref sharedColorSTSaturationDefaults);
            RestoreDefault(ref sharedColorSTBrightnessDefaults);

            RestoreDefault(ref sharedMaterialSBDefaults);
            RestoreDefault(ref sharedColorSBOpacityDefaults);
            RestoreDefault(ref sharedColorSBHueDefaults);
            RestoreDefault(ref sharedColorSBSaturationDefaults);
            RestoreDefault(ref sharedColorSBBrightnessDefaults);

            RestoreDefault(ref sharedMaterialETDefaults);
            RestoreDefault(ref sharedColorETOpacityDefaults);
            RestoreDefault(ref sharedColorETHueDefaults);
            RestoreDefault(ref sharedColorETSaturationDefaults);
            RestoreDefault(ref sharedColorETBrightnessDefaults);

            RestoreDefault(ref sharedMaterialELDefaults);
            RestoreDefault(ref sharedColorELOpacityDefaults);
            RestoreDefault(ref sharedColorELHueDefaults);
            RestoreDefault(ref sharedColorELSaturationDefaults);
            RestoreDefault(ref sharedColorELBrightnessDefaults);
        }

        private void RestoreDefault(ref Vector4 set)
        {
            set = new Vector4(set.z, set.w, set.z, set.w);
        }

        private float GetDefault(Vector4 set)
        {
            return isCtrlSrf ? set.y : set.x;
        }

        #endregion Default values

        #region Lift configuration switching

        // Has to be situated here as this KSPEvent is not correctly added Part.Events otherwise
        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000163", active = true)]		// #autoLOC_B9_Aerospace_WingStuff_1000163 = Surface Config: Lifting
        public void ToggleLiftConfiguration()
        {

            if (!CanBeFueled || assemblyFARUsed)
            {
                return;
            }

            aeroIsLiftingSurface = !aeroIsLiftingSurface;
            LiftStructuralTypeChanged();
        }

        #endregion Lift configuration switching

        #region Fuel configuration switching

        // Has to be situated here as this KSPEvent is not correctly added Part.Events otherwise
        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000132", active = true)]		// #autoLOC_B9_Aerospace_WingStuff_1000132 = Next configuration
        public void NextConfiguration()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFuel)
            {
                DebugLogWithID("NextConfiguration", "Started");
            }

            if (!(CanBeFueled && UseStockFuel))
            {
                return;
            }

            fuelSelectedTankSetup++;

            if (fuelSelectedTankSetup >= StaticWingGlobals.wingTankConfigurations.Count)
            {
                fuelSelectedTankSetup = 0;
            }

            FuelTankTypeChanged();
        }

        #endregion Fuel configuration switching

        #region Inheritance

        private bool inheritancePossibleOnShape = false;
        private bool inheritancePossibleOnMaterials = false;
        private void InheritanceStatusUpdate()
        {
            if (part.parent == null)
            {
                return;
            }

            if (!part.parent.Modules.Contains<WingProcedural>())
            {
                return;
            }

            WingProcedural parentModule = FirstOfTypeOrDefault<WingProcedural>(part.parent.Modules);
            if (parentModule != null)
            {
                if (!parentModule.isCtrlSrf)
                {
                    inheritancePossibleOnMaterials = true;
                    inheritancePossibleOnShape |= !isCtrlSrf;
                }
            }
        }

        private void InheritParentValues(int mode, bool back = false)
        {
            if (part.parent == null)
            {
                return;
            }

            if (!part.parent.Modules.Contains<WingProcedural>())
            {
                return;
            }

            WingProcedural parentModule = FirstOfTypeOrDefault<WingProcedural>(part.parent.Modules);
            if (parentModule == null)
            {
                return;
            }

            switch (mode)
            {
                case 0:
                    InheritShape(parentModule);
                    break;

                case 1:
                    InheritBase(parentModule);
                    break;

                case 2:
                    InheritEdges(parentModule);
                    break;

                case 3:
                    InheritColours(parentModule);
                    break;
                case 4:
                    InheritCtrlOffset(parentModule, back);
                    break;
            }
        }

        private void InheritShape(WingProcedural parent)
        {
            if (parent.isCtrlSrf || isCtrlSrf)
                return;

            if (Input.GetMouseButtonUp(0))
                InheritBase(parent);
            sharedBaseThicknessRoot = parent.sharedBaseThicknessTip;

            float tip = sharedBaseWidthRoot + ((parent.sharedBaseWidthTip - parent.sharedBaseWidthRoot) / (parent.sharedBaseLength)) * sharedBaseLength;
            if (sharedBaseWidthTip < 0)
                sharedBaseLength *= sharedBaseWidthRoot / (sharedBaseWidthRoot - sharedBaseWidthTip);
            float offset = sharedBaseLength / parent.sharedBaseLength * parent.sharedBaseOffsetTip;
            sharedBaseWidthTip = tip;
            sharedBaseOffsetTip = offset;
            sharedBaseThicknessTip = Mathf.Min(sharedBaseThicknessRoot + (float)(sharedBaseLength / parent.sharedBaseLength) * (float)(parent.sharedBaseThicknessTip - parent.sharedBaseThicknessRoot), 0); //use mathf.Min instead of define the function min
        }

        private void InheritBase(WingProcedural parent)
        {
            if (parent.isCtrlSrf || isCtrlSrf)
                return;

            sharedBaseWidthRoot = parent.sharedBaseWidthTip;
            sharedBaseThicknessRoot = parent.sharedBaseThicknessTip;

            sharedBaseOffsetRoot = -parent.sharedBaseOffsetTip;

            sharedEdgeTypeLeading = parent.sharedEdgeTypeLeading;
            sharedEdgeWidthLeadingRoot = parent.sharedEdgeWidthLeadingTip;

            sharedEdgeTypeTrailing = parent.sharedEdgeTypeTrailing;
            sharedEdgeWidthTrailingRoot = parent.sharedEdgeWidthTrailingTip;
        }

        private void InheritEdges(WingProcedural parent)
        {
            if (parent.isCtrlSrf || isCtrlSrf)
                return;

            sharedEdgeTypeLeading = parent.sharedEdgeTypeLeading;
            sharedEdgeWidthLeadingRoot = parent.sharedEdgeWidthLeadingTip;
            sharedEdgeWidthLeadingTip = Mathf.Clamp(sharedEdgeWidthLeadingRoot + ((parent.sharedEdgeWidthLeadingTip - parent.sharedEdgeWidthLeadingRoot) / parent.sharedBaseLength) * sharedBaseLength, sharedEdgeWidthLimits.x, sharedEdgeWidthLimits.y);

            sharedEdgeTypeTrailing = parent.sharedEdgeTypeTrailing;
            sharedEdgeWidthTrailingRoot = parent.sharedEdgeWidthTrailingTip;
            sharedEdgeWidthTrailingTip = Mathf.Clamp(sharedEdgeWidthTrailingRoot + ((parent.sharedEdgeWidthTrailingTip - parent.sharedEdgeWidthTrailingRoot) / parent.sharedBaseLength) * sharedBaseLength, sharedEdgeWidthLimits.x, sharedEdgeWidthLimits.y);
        }

        private void InheritColours(WingProcedural parent)
        {
            sharedMaterialST = parent.sharedMaterialST;
            sharedColorSTOpacity = parent.sharedColorSTOpacity;
            sharedColorSTHue = parent.sharedColorSTHue;
            sharedColorSTSaturation = parent.sharedColorSTSaturation;
            sharedColorSTBrightness = parent.sharedColorSTBrightness;

            sharedMaterialSB = parent.sharedMaterialSB;
            sharedColorSBOpacity = parent.sharedColorSBOpacity;
            sharedColorSBHue = parent.sharedColorSBHue;
            sharedColorSBSaturation = parent.sharedColorSBSaturation;
            sharedColorSBBrightness = parent.sharedColorSBBrightness;

            sharedMaterialET = parent.sharedMaterialET;
            sharedColorETOpacity = parent.sharedColorETOpacity;
            sharedColorETHue = parent.sharedColorETHue;
            sharedColorETSaturation = parent.sharedColorETSaturation;
            sharedColorETBrightness = parent.sharedColorETBrightness;

            sharedMaterialEL = parent.sharedMaterialEL;
            sharedColorELOpacity = parent.sharedColorELOpacity;
            sharedColorELHue = parent.sharedColorELHue;
            sharedColorELSaturation = parent.sharedColorELSaturation;
            sharedColorELBrightness = parent.sharedColorELBrightness;
        }

        private void InheritCtrlOffset(WingProcedural parent, bool back)
        {
            if (back)
            {
                float trueoffset = (parent.sharedBaseOffsetTip + parent.sharedBaseWidthTip / 2 - parent.sharedBaseWidthRoot / 2) / parent.sharedBaseLength;
                sharedBaseOffsetRoot = trueoffset;
                sharedBaseOffsetTip = trueoffset;

            }
            else
            {
                float trueoffset = (-parent.sharedBaseOffsetTip + parent.sharedBaseWidthTip / 2 - parent.sharedBaseWidthRoot / 2) / parent.sharedBaseLength;
                sharedBaseOffsetRoot = trueoffset;
                sharedBaseOffsetTip = trueoffset;
            }
        }

        #endregion Inheritance

        #region Mod detection

        public static bool assembliesChecked = false;
        public static bool assemblyFARUsed = false;
        public static bool assemblyRFUsed = false;
        public static bool assemblyMFTUsed = false;
        // if current part uses one of the Configurable Container modules
        public bool moduleCCUsed = false;

        public void CheckAssemblies()
        {
            // check for Configurable Containers modules in this part.
            // check for .dll cannot be used because ConfigurableContainers.dll is part of AT_Utils
            // and is destributed without MM patches that add these modules to parts
            // per part check run every time
            moduleCCUsed = part.Modules.Contains("ModuleSwitchableTank") || part.Modules.Contains("ModuleTankManager");
            if (!assembliesChecked)
            {
                foreach (AssemblyLoader.LoadedAssembly test in AssemblyLoader.loadedAssemblies)
                {
                    if (test.assembly.GetName().Name.Equals("FerramAerospaceResearch", StringComparison.InvariantCultureIgnoreCase))
                    {
                        assemblyFARUsed = true;
                        CtrlSrfWingSynchronizer.InitFAR();
                    }
                    else if (test.assembly.GetName().Name.Equals("RealFuels", StringComparison.InvariantCultureIgnoreCase))
                    {
                        assemblyRFUsed = true;
                    }
                    else if (test.assembly.GetName().Name.Equals("modularFuelTanks", StringComparison.InvariantCultureIgnoreCase))
                    {
                        assemblyMFTUsed = true;
                    }
                }
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
                {
                    DebugLogWithID("CheckAssemblies", "Search results | FAR: " + assemblyFARUsed + " | RF: " + assemblyRFUsed + " | MFT: " + assemblyMFTUsed);
                }
                assembliesChecked = true;
            }
            int mod_conflict = Convert.ToInt32(assemblyMFTUsed) + Convert.ToInt32(assemblyRFUsed) + Convert.ToInt32(moduleCCUsed);

            // check for more than one dynamic tank mod in use
            if (isCtrlSrf && isWingAsCtrlSrf && HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("CheckAssemblies", "WARNING | PART IS CONFIGURED INCORRECTLY, BOTH BOOL PROPERTIES SHOULD NEVER BE SET TO TRUE");
            }

            if (mod_conflict > 1 && HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("CheckAssemblies", "WARNING | More than one of RF, MFT and CC mods detected, this should not be the case");
            }

            //update part events
            if (Events != null)
            {
                Events["NextConfiguration"].active = UseStockFuel;
            }
        }

        #endregion Mod detection

        #region Unity stuff and Callbacks/events

        public bool isStarted = false;
        /// <summary>
        /// run when part is created in editor, and when part is created in flight. Why is OnStart and Start both being used other than to sparate flight and editor startup?
        /// </summary>
        public override void OnStart(PartModule.StartState state)
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("OnStart", "Invoked");
            }

            base.OnStart(state);
            CheckAssemblies();

            if (!HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            DebugLogWithID("OnStart", "Setup started");
            StartCoroutine(SetupReorderedForFlight()); // does all setup neccesary for flight
            isStarted = true;
            GameEvents.onGameSceneLoadRequested.Add(OnSceneSwitch);
        }
        public List<WingProcedural> procws;
        /// <summary>
        /// run whenever part is created (used in editor), which in the editor is as soon as part list is clicked or symmetry count increases
        /// </summary>
        public void Start()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("Start", "Invoked");
            }

            if (!HighLogic.LoadedSceneIsEditor)
            {
                if (HighLogic.LoadedSceneIsFlight && isWingAsCtrlSrf) FindConnectedCtrlSrfWings();
                return;
            }

            GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);

            uiInstanceIDLocal = uiInstanceIDTarget = 0;

            Setup();
            part.OnEditorAttach += new Callback(UpdateOnEditorAttach);
            part.OnEditorDetach += new Callback(UpdateOnEditorDetach);

            if (!UIUtility.uiStyleConfigured)
            {
                UIUtility.ConfigureStyles();
            }
            isStarted = true;
        }

        // unnecesary save/load. config is static so it will be initialised as you pass through the space center, and there is no way to change options in the editor scene
        // may resolve errors reported by Hodo
        public override void OnSave(ConfigNode node)
        {
            // try...catch block for a method that just loves to throw and kill the onsave callback chain (there's nothing throwing there atm, doesn't mean it will always be the way)
            try
            {
                node.RemoveValues("mirrorTexturing");
                node.AddValue("mirrorTexturing", isMirrored);
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
                {
                    DebugLogWithID("OnSave", "Invoked");
                }
                foreach (VesselStatus v in vesselList)
                {
                    if (v.vessel == vessel)
                    {
                        v.isUpdated = false;
                    }
                }
            }
            catch
            {
                Debug.Log("B9 PWings - Failed to save settings");
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            node.TryGetValue("mirrorTexturing", ref isMirrored);

            if (HighLogic.LoadedScene != GameScenes.LOADING && HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("OnLoad", "Invoked");
            }
        }

        public void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneSwitch);
            GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
        }

        public void CalcBase(int fieldID) //Calculate Geometry from angle ,originally by Rynco Lee modified by tetraflon, higher precision is required for angle calculations thus use Math double
        {
            float AngleFront;
            float AngleBack;
            if (sharedPropEdgePref == true)//Get angles without edges from those with edges
            {
                AngleFront = (float)(Math.Atan(sharedBaseLength / (sharedBaseLength / Math.Tan(sharedSweptAngleFront * Mathf.Deg2Rad) - (sharedEdgeWidthLeadingRoot - sharedEdgeWidthLeadingTip))) / Mathf.Deg2Rad);
                AngleBack = (float)(Math.Atan(sharedBaseLength / (sharedBaseLength / Math.Tan(sharedSweptAngleBack * Mathf.Deg2Rad) + (sharedEdgeWidthTrailingRoot - sharedEdgeWidthTrailingTip))) / Mathf.Deg2Rad);
            }
            else
            {
                AngleFront = sharedSweptAngleFront;
                AngleBack = sharedSweptAngleBack;
            }
            if (!sharedPropLockPref && !sharedPropLock3Pref)
            {
                sharedBaseWidthTip = (float)(sharedBaseWidthRoot - 1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength + 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength);
                //sharedBaseOffsetTip = (float)((1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength + 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength) / 2 - sharedBaseOffsetRoot);
            }
            else if (sharedPropLockPref && !sharedPropLock3Pref)
            {
                sharedBaseWidthRoot = (float)(sharedBaseWidthTip + 1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength - 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength);
                //sharedBaseOffsetRoot = (float)((1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength + 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength) / 2 - sharedBaseOffsetTip);
            }
            if (sharedPropLock2Pref)
            {
                if (sharedPropLock3Pref)
                {
                    if (fieldID == 201)
                    {
                        sharedBaseOffsetRoot = (float)(sharedBaseLength / Math.Tan(AngleFront * Mathf.Deg2Rad) - sharedBaseWidthRoot / 2 + sharedBaseWidthTip / 2 - sharedBaseOffsetTip);
                    }
                    else if (fieldID == 202)
                    {
                        sharedBaseOffsetRoot = (float)(sharedBaseLength / Math.Tan(AngleBack * Mathf.Deg2Rad) + sharedBaseWidthRoot / 2 - sharedBaseWidthTip / 2 - sharedBaseOffsetTip);
                    }
                }
                else
                {
                    sharedBaseOffsetRoot = (float)((1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength + 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength) / 2 - sharedBaseOffsetTip);
                }
            }
            else if (!sharedPropLock2Pref)
            {
                if (sharedPropLock3Pref)
                {
                    if (fieldID == 201)
                    {
                        sharedBaseOffsetTip = (float)(sharedBaseLength / Math.Tan(AngleFront * Mathf.Deg2Rad) - sharedBaseWidthRoot / 2 + sharedBaseWidthTip / 2 - sharedBaseOffsetRoot);
                    }
                    else if (fieldID == 202)
                    {
                        sharedBaseOffsetTip = (float)(sharedBaseLength / Math.Tan(AngleBack * Mathf.Deg2Rad) + sharedBaseWidthRoot / 2 - sharedBaseWidthTip / 2 - sharedBaseOffsetRoot);
                    }
                }
                else
                {
                    sharedBaseOffsetTip = (float)((1 / (Math.Tan(Mathf.Deg2Rad * AngleFront)) * sharedBaseLength + 1 / (Math.Tan(Mathf.Deg2Rad * AngleBack)) * sharedBaseLength) / 2 - sharedBaseOffsetRoot);
                }
            }

            if (sharedBaseWidthRoot < 0)
            {
                if (!sharedPropLock2Pref)
                {
                    if (fieldID == 201)
                    {
                        sharedBaseOffsetTip -= sharedBaseWidthRoot / 2;
                        sharedBaseWidthRoot = 0;
                    }
                    else if (fieldID == 202)
                    {
                        sharedBaseOffsetTip += sharedBaseWidthRoot / 2;
                        sharedBaseWidthRoot = 0;
                    }
                }
                else if (sharedPropLock2Pref)
                {
                    if (fieldID == 201)
                    {
                        sharedBaseOffsetRoot -= sharedBaseWidthRoot / 2;
                        sharedBaseWidthRoot = 0;
                    }
                    else if (fieldID == 202)
                    {
                        sharedBaseOffsetRoot += sharedBaseWidthRoot / 2;
                        sharedBaseWidthRoot = 0;
                    }
                }
            }
            if (sharedBaseWidthTip < 0) //detect which value is being editing and handle the exceptional cases
            {
                if (fieldID == 201)
                {
                    if (sharedPropEdgePref == true)
                    {
                        sharedEdgeWidthLeadingTip += sharedBaseWidthTip / 2;

                        sharedBaseWidthTip = 0f;
                        if (sharedEdgeWidthLeadingTip < 0)
                        {
                            sharedBaseOffsetTip -= sharedEdgeWidthLeadingTip;
                            sharedEdgeWidthLeadingTip = 0f;
                            //DebugLogWithID("Angle Calculation", "Forward override");
                        }
                    }
                    else
                    {
                        sharedBaseOffsetTip -= sharedBaseWidthTip / 2;
                        sharedBaseWidthTip = 0f;
                    }
                    //DebugLogWithID("Angle Calculation", "Forward override");
                }
                if (fieldID == 202)
                {
                    if (sharedPropEdgePref == true)
                    {
                        sharedEdgeWidthTrailingTip += sharedBaseWidthTip / 2;

                        sharedBaseWidthTip = 0f;
                        if (sharedEdgeWidthTrailingTip < 0)
                        {
                            sharedBaseOffsetTip += sharedEdgeWidthTrailingTip;
                            sharedEdgeWidthTrailingTip = 0f;
                            //DebugLogWithID("Angle Calculation", "Backward override");
                        }
                    }
                    else
                    {
                        sharedBaseOffsetTip += sharedBaseWidthTip / 2;
                        sharedBaseWidthTip = 0f;
                    }
                    //DebugLogWithID("Angle Calculation", "Backward override");
                }
            }

        }
        // Split Angle Calculations into two half, since no need to update the editing value
        public float CalcAngleFront()
        {
            float modifier;
            float AngleFront;
            if (sharedPropEdgePref == true)
            {
                modifier = sharedEdgeWidthLeadingRoot - sharedEdgeWidthLeadingTip;
            }
            else
            {
                modifier = 0;
            }
            AngleFront = (float)Math.Atan(sharedBaseLength / (sharedBaseWidthRoot / 2 - sharedBaseWidthTip / 2 + sharedBaseOffsetTip + sharedBaseOffsetRoot + modifier)) / Mathf.Deg2Rad;
            if (AngleFront < 0)
            {
                AngleFront += 180;
            }
            return AngleFront;
        }

        public float CalcAngleBack()
        {
            float modifier;
            float AngleBack;
            if (sharedPropEdgePref == true)
            {
                modifier = sharedEdgeWidthTrailingTip - sharedEdgeWidthTrailingRoot;
            }
            else
            {
                modifier = 0;
            }
            AngleBack = (float)Math.Atan(sharedBaseLength / (-sharedBaseWidthRoot / 2 + sharedBaseWidthTip / 2 + sharedBaseOffsetTip + sharedBaseOffsetRoot + modifier)) / Mathf.Deg2Rad;
            if (AngleBack < 0)
            {
                AngleBack += 180;
            }
            return AngleBack;
        }
        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor || !isStarted)
            {
                return;
            }

            DebugTimerUpdate();
            UpdateUI();

            DeformWing();
            CheckAllFieldValues(out bool updateGeo, out bool updateAero);

            if (part.GetInstanceID() == uiInstanceIDTarget)
                UpdateHandleGizmos();

            if (updateGeo)
            {
                UpdateGeometry(updateAero);
                UpdateCounterparts();
            }
        }

        // Attachment handling
        public void UpdateOnEditorAttach()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("UpdateOnEditorAttach", "Setup started");
            }

            isMirrored =
                (part.symMethod == SymmetryMethod.Mirror)
                &&
                Vector3.Dot(EditorLogic.SortedShipList[0].transform.right, part.transform.position - EditorLogic.SortedShipList[0].transform.position) < 0
            ;

            isAttached = true;
            UpdateGeometry(true);
            SetupMirroredCntrlSrf();

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logEvents)
            {
                DebugLogWithID("UpdateOnEditorAttach", "Setup ended");
            }
        }

        public void UpdateOnEditorDetach()
        {
            if (part.parent != null && part.parent.Modules.Contains<WingProcedural>())
            {
                WingProcedural parentModule = FirstOfTypeOrDefault<WingProcedural>(part.parent.Modules);
                if (parentModule != null)
                {
                    parentModule.FuelVolumeChanged();
                    parentModule.CalculateAerodynamicValues();
                }
            }

            isAttached = false;
            uiEditMode = false;
        }


        /// <summary>
        /// Make possible to attach one all-moving wing to another
        /// </summary>
        public void OnEditorPartEvent(ConstructionEventType type, Part p)
        {
            if (isWingAsCtrlSrf)
            {
                if (type == ConstructionEventType.PartCopied || type == ConstructionEventType.PartPicked || type == ConstructionEventType.PartCreated || type == ConstructionEventType.PartDetached)
                    if (p.name.StartsWith("B9.Aero.Wing.Procedural.TypeC"))
                    {
                        var wproc = FirstOfTypeOrDefault<WingProcedural>(p.Modules);
                        if (wproc && wproc.isWingAsCtrlSrf)
                            part.attachRules.allowSrfAttach = true;
                        else
                            part.attachRules.allowSrfAttach = false;
                    }
                    else
                        part.attachRules.allowSrfAttach = false;
            }
            if (p.name.StartsWith("B9.Aero.Wing.Procedural") && sharedArmorPref)
            {
                part.crashTolerance = 15 + sharedArmorRatio;
            }
        }

        private bool connectedCtrlSrfWingsChecked = false;
        /// <summary>
        /// Find all connected all-moving wings, and add a plugin to sync their defelctions (called on flight start)
        /// </summary>
        public void FindConnectedCtrlSrfWings()
        {
            if (connectedCtrlSrfWingsChecked)
                return;
            connectedCtrlSrfWingsChecked = true;

            List<WingProcedural> connectedCtrlSrfWings = new List<WingProcedural>() { this };

            //Find connected all-moving wing's root
            var ctrlSrfWingRoot = part;
            do
            {
                if (!ctrlSrfWingRoot.parent || !ctrlSrfWingRoot.parent.name.StartsWith("B9.Aero.Wing.Procedural.TypeC"))
                    break;
                var temp = ctrlSrfWingRoot;
                ctrlSrfWingRoot = ctrlSrfWingRoot.parent;
                var wp = FirstOfTypeOrDefault<WingProcedural>(ctrlSrfWingRoot.Modules);
                if (!wp || wp.connectedCtrlSrfWingsChecked)
                {
                    ctrlSrfWingRoot = temp;
                    break;
                }
            } while (true);

            //Find all connected all-moving wings 
            IEnumerable<Part> ctrlSrfWingParts = ctrlSrfWingRoot.children.Where(c => c && c.name.StartsWith("B9.Aero.Wing.Procedural.TypeC"));
            foreach (var p in ctrlSrfWingParts.ToList())
            {
                IEnumerable<Part> second = p.children.Where(c => c && c.name.StartsWith("B9.Aero.Wing.Procedural.TypeC"));
                if (second.Count() > 0)
                {
                    ctrlSrfWingParts = ctrlSrfWingParts.Concat(second);
                    foreach (var pp in second.ToList())
                        ctrlSrfWingParts = ctrlSrfWingParts.Concat(pp.children.Where(c => c && c.name.StartsWith("B9.Aero.Wing.Procedural.TypeC")));
                }
            }

            var childrenCtrlSrfWings = ctrlSrfWingParts
                    .Select(c => FirstOfTypeOrDefault<WingProcedural>(c.Modules))
                    .Where(wp => wp && wp.isWingAsCtrlSrf);

            //Check, then add a synchronizer for connected all-moving wings
            foreach (var wp in childrenCtrlSrfWings)
                if (!wp.connectedCtrlSrfWingsChecked)
                {
                    //rotation axis is aligned
                    if ((wp.transform.right - transform.right).magnitude < 0.05f)
                    {
                        wp.connectedCtrlSrfWingsChecked = true;
                        connectedCtrlSrfWings.Add(wp);
                    }
                }

            if (connectedCtrlSrfWings.Count > 1)
            {
#if FAR
                if (assemblyFARUsed) CtrlSrfWingSynchronizer.FARAddSynchronizer(ctrlSrfWingRoot, connectedCtrlSrfWings);
                else
#endif
                    CtrlSrfWingSynchronizer.AddSynchronizer(ctrlSrfWingRoot, connectedCtrlSrfWings);
            }
        }
        public void OnSceneSwitch(GameScenes scene)
        {
            isStarted = false; // fixes annoying nullrefs when switching scenes and things haven't been destroyed yet
            editorCam = null;
        }

        /// <summary>
        /// called by Start routines of editor and flight scenes
        /// </summary>
        public void Setup()
        {
            SetupFields();
            FuelStart(); // shifted from Setup() to fix NRE caused by reattaching a single part that wasn't originally mirrored. Shifted back now Setup is called from Start
            RefreshGeometry();
        }

        /// <summary>
        /// called from setup and when updating clones
        /// </summary>
        public void RefreshGeometry()
        {
            SetupMeshFilters();
            SetupMeshReferences();
            ReportOnMeshReferences();
            if (ApplyLegacyTextures())
            {
                UpdateMaterials();
            }
            UpdateGeometry(true);
            UpdateWindow();
        }
        private bool ApplyLegacyTextures()
        {
            return part.GetComponent("KSPTextureSwitch") == null;
        }

        #endregion Unity stuff and Callbacks/events

        #region Geometry

        public void UpdateGeometry(bool updateAerodynamics)
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
            {
                DebugLogWithID("UpdateGeometry", "Started | isCtrlSrf: " + isCtrlSrf);
            }

            float geometricLength = sharedBaseLength / part.rescaleFactor;

            if (!isCtrlSrf)
            {
                float wingThicknessDeviationRoot = (sharedBaseThicknessRoot / 0.24f) / part.rescaleFactor;
                float wingThicknessDeviationTip = (sharedBaseThicknessTip / 0.24f) / part.rescaleFactor;
                float wingWidthTipBasedOffsetTrailing = (sharedBaseWidthTip / 2f + sharedBaseOffsetTip) / part.rescaleFactor;
                float wingWidthTipBasedOffsetLeading = (-sharedBaseWidthTip / 2f + sharedBaseOffsetTip) / part.rescaleFactor;
                float wingWidthRoot = (sharedBaseWidthRoot / 2f) / part.rescaleFactor;
                float wingWidthRootBasedOffset = -sharedBaseOffsetRoot / part.rescaleFactor;
                float geometricWidthTip = sharedBaseWidthTip / part.rescaleFactor;
                float geometricWidthRoot = sharedBaseWidthRoot / part.rescaleFactor;
                float geometricOffsetTip = sharedBaseOffsetTip / part.rescaleFactor;

                // First, wing cross section
                // No need to filter vertices by normals

                if (meshFilterWingSection != null)
                {
                    int length = meshReferenceWingSection.vp.Length;
                    Vector3[] vp = new Vector3[length];
                    Array.Copy(meshReferenceWingSection.vp, vp, length);
                    Vector2[] uv = new Vector2[length];
                    Array.Copy(meshReferenceWingSection.uv, uv, length);
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Wing section | Passed array setup");
                    }

                    for (int i = 0; i < length; ++i)
                    {
                        // Root/tip filtering followed by leading/trailing filtering
                        if (vp[i].x < -0.05f)
                        {
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, wingWidthTipBasedOffsetLeading);
                                uv[i] = new Vector2(geometricWidthTip, uv[i].y);
                            }
                            else
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, wingWidthTipBasedOffsetTrailing);
                                uv[i] = new Vector2(0f, uv[i].y);
                            }
                        }
                        else
                        {
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y * wingThicknessDeviationRoot, wingWidthRootBasedOffset - wingWidthRoot);
                                uv[i] = new Vector2(geometricWidthRoot, uv[i].y);
                            }
                            else
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y * wingThicknessDeviationRoot, wingWidthRootBasedOffset + wingWidthRoot);
                                uv[i] = new Vector2(0f, uv[i].y);
                            }
                        }
                    }

                    meshFilterWingSection.mesh.vertices = vp;
                    meshFilterWingSection.mesh.uv = uv;
                    meshFilterWingSection.mesh.RecalculateBounds();


                    MeshCollider meshCollider = meshFilterWingSection.gameObject.GetComponent<MeshCollider>();

                    if (meshCollider == null)
                    {
                        meshCollider = meshFilterWingSection.gameObject.AddComponent<MeshCollider>();
                    }

                    meshCollider.sharedMesh = null;
                    meshCollider.sharedMesh = meshFilterWingSection.mesh;
                    meshCollider.convex = true;

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Wing section | Finished");
                    }
                }

                // Second, wing surfaces
                // Again, no need for filtering by normals

                if (meshFilterWingSurface != null)
                {
                    meshFilterWingSurface.transform.localPosition = Vector3.zero;
                    meshFilterWingSurface.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

                    int length = meshReferenceWingSurface.vp.Length;
                    Vector3[] vp = new Vector3[length];
                    Array.Copy(meshReferenceWingSurface.vp, vp, length);
                    Vector2[] uv = new Vector2[length];
                    Array.Copy(meshReferenceWingSurface.uv, uv, length);
                    Color[] cl = new Color[length];
                    Vector2[] uv2 = new Vector2[length];

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Wing surface top | Passed array setup");
                    }

                    for (int i = 0; i < length; ++i)
                    {
                        // Root/tip filtering followed by leading/trailing filtering
                        if (vp[i].x < -0.05f)
                        {
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, wingWidthTipBasedOffsetLeading);
                                uv[i] = new Vector2(geometricLength / 4f, 1f - 0.5f + geometricWidthTip / 8f - geometricOffsetTip / 4f);
                            }
                            else
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, wingWidthTipBasedOffsetTrailing);
                                uv[i] = new Vector2(geometricLength / 4f, 0f + 0.5f - geometricWidthTip / 8f - geometricOffsetTip / 4f);
                            }
                        }
                        else
                        {
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y * wingThicknessDeviationRoot, wingWidthRootBasedOffset - wingWidthRoot);
                                uv[i] = new Vector2(0.0f, 1f - 0.5f + (-wingWidthRootBasedOffset * 2f + geometricWidthRoot) / 8f);
                            }
                            else
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y * wingThicknessDeviationRoot, wingWidthRootBasedOffset + wingWidthRoot);
                                uv[i] = new Vector2(0f, 0f + 0.5f - (+wingWidthRootBasedOffset * 2f + geometricWidthRoot) / 8f);
                            }
                        }

                        // Top/bottom filtering
                        if (vp[i].y > 0f ^ isMirrored)
                        {
                            cl[i] = GetVertexColor(0);
                            uv2[i] = GetVertexUV2(sharedMaterialST);
                        }
                        else
                        {
                            cl[i] = GetVertexColor(1);
                            uv2[i] = GetVertexUV2(sharedMaterialSB);
                        }
                    }

                    meshFilterWingSurface.mesh.vertices = vp;
                    meshFilterWingSurface.mesh.uv = uv;
                    meshFilterWingSurface.mesh.uv2 = uv2;
                    meshFilterWingSurface.mesh.colors = cl;
                    meshFilterWingSurface.mesh.RecalculateBounds();

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Wing surface | Finished");
                    }
                }

                // Next, time for leading and trailing edges
                // Before modifying geometry, we have to show the correct objects for the current selection
                // As UI only works with floats, we have to cast selections into ints too

                int wingEdgeTypeTrailingInt = Mathf.RoundToInt(sharedEdgeTypeTrailing - 1);
                int wingEdgeTypeLeadingInt = Mathf.RoundToInt(sharedEdgeTypeLeading - 1);

                for (int i = 0; i < meshTypeCountEdgeWing; ++i)
                {
                    if (i != wingEdgeTypeTrailingInt)
                    {
                        meshFiltersWingEdgeTrailing[i].gameObject.SetActive(false);
                    }
                    else
                    {
                        meshFiltersWingEdgeTrailing[i].gameObject.SetActive(true);
                    }

                    if (i != wingEdgeTypeLeadingInt)
                    {
                        meshFiltersWingEdgeLeading[i].gameObject.SetActive(false);
                    }
                    else
                    {
                        meshFiltersWingEdgeLeading[i].gameObject.SetActive(true);
                    }
                }

                // Next we calculate some values reused for all edge geometry

                float wingEdgeWidthLeadingRootDeviation = sharedEdgeWidthLeadingRoot / 0.24f / part.rescaleFactor;
                float wingEdgeWidthLeadingTipDeviation = sharedEdgeWidthLeadingTip / 0.24f / part.rescaleFactor;

                float wingEdgeWidthTrailingRootDeviation = sharedEdgeWidthTrailingRoot / 0.24f / part.rescaleFactor;
                float wingEdgeWidthTrailingTipDeviation = sharedEdgeWidthTrailingTip / 0.24f / part.rescaleFactor;

                // Next, we fetch appropriate mesh reference and mesh filter for the edges and modify the meshes
                // Geometry is split into groups through simple vertex normal filtering

                // We must update the meshes for all of the trailing edge types, not just the active one
                // Otherwise the module's size will over-report by the bounds of the largest mesh
                for (int j = 0; j < meshTypeCountEdgeWing; j++)
                {

                    if (meshFiltersWingEdgeTrailing[j] != null)
                    {
                        MeshReference meshReference = meshReferencesWingEdge[j];
                        int length = meshReference.vp.Length;
                        Vector3[] vp = new Vector3[length];
                        Array.Copy(meshReference.vp, vp, length);
                        Vector3[] nm = new Vector3[length];
                        Array.Copy(meshReference.nm, nm, length);
                        Vector2[] uv = new Vector2[length];
                        Array.Copy(meshReference.uv, uv, length);
                        Color[] cl = new Color[length];
                        Vector2[] uv2 = new Vector2[length];


                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                        {
                            DebugLogWithID("UpdateGeometry", "Wing edge trailing | Passed array setup");
                        }

                        for (int i = 0; i < vp.Length; ++i)
                        {
                            if (vp[i].x < -0.1f)
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, vp[i].z * wingEdgeWidthTrailingTipDeviation + geometricWidthTip / 2f + geometricOffsetTip); // Tip edge
                                if (nm[i].x == 0f)
                                {
                                    uv[i] = new Vector2(geometricLength, uv[i].y);
                                }
                            }
                            else
                            {
                                vp[i] = new Vector3(0f, vp[i].y * wingThicknessDeviationRoot, vp[i].z * wingEdgeWidthTrailingRootDeviation + geometricWidthRoot / 2f + wingWidthRootBasedOffset); // Root edge
                            }

                            if (nm[i].x == 0f && sharedEdgeTypeTrailing != 1)
                            {
                                cl[i] = GetVertexColor(2);
                                uv2[i] = GetVertexUV2(sharedMaterialET);
                            }
                        }

                        meshFiltersWingEdgeTrailing[j].mesh.vertices = vp;
                        meshFiltersWingEdgeTrailing[j].mesh.uv = uv;
                        meshFiltersWingEdgeTrailing[j].mesh.uv2 = uv2;
                        meshFiltersWingEdgeTrailing[j].mesh.colors = cl;
                        meshFiltersWingEdgeTrailing[j].mesh.RecalculateBounds();
                    }
                }

                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)

                {
                    DebugLogWithID("UpdateGeometry", "Wing edge trailing | Finished");
                }

                // We must update the meshes for all of the leading edge types, not just the active one
                // Otherwise the module's size will over-report by the bounds of the largest mesh
                for (int j = 0; j < meshTypeCountEdgeWing; j++)
                {
                    if (meshFiltersWingEdgeLeading[j] != null)
                    {
                        MeshReference meshReference = meshReferencesWingEdge[j];
                        int length = meshReference.vp.Length;
                        Vector3[] vp = new Vector3[length];
                        Array.Copy(meshReference.vp, vp, length);
                        Vector3[] nm = new Vector3[length];
                        Array.Copy(meshReference.nm, nm, length);
                        Vector2[] uv = new Vector2[length];
                        Array.Copy(meshReference.uv, uv, length);
                        Color[] cl = new Color[length];
                        Vector2[] uv2 = new Vector2[length];

                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                        {
                            DebugLogWithID("UpdateGeometry", "Wing edge leading | Passed array setup");
                        }

                        for (int i = 0; i < vp.Length; ++i)
                        {
                            if (vp[i].x < -0.1f)
                            {
                                vp[i] = new Vector3(-geometricLength, vp[i].y * wingThicknessDeviationTip, vp[i].z * wingEdgeWidthLeadingTipDeviation + geometricWidthTip / 2f - geometricOffsetTip); // Tip edge
                                if (nm[i].x == 0f)
                                {
                                    uv[i] = new Vector2(geometricLength, uv[i].y);
                                }
                            }
                            else
                            {
                                vp[i] = new Vector3(0f, vp[i].y * wingThicknessDeviationRoot, vp[i].z * wingEdgeWidthLeadingRootDeviation + geometricWidthRoot / 2f - wingWidthRootBasedOffset); // Root edge
                            }

                            if (nm[i].x == 0f && sharedEdgeTypeLeading != 1)
                            {
                                cl[i] = GetVertexColor(3);
                                uv2[i] = GetVertexUV2(sharedMaterialEL);
                            }
                        }

                        meshFiltersWingEdgeLeading[j].mesh.vertices = vp;
                        meshFiltersWingEdgeLeading[j].mesh.uv = uv;
                        meshFiltersWingEdgeLeading[j].mesh.uv2 = uv2;
                        meshFiltersWingEdgeLeading[j].mesh.colors = cl;
                        meshFiltersWingEdgeLeading[j].mesh.RecalculateBounds();
                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                        {
                            DebugLogWithID("UpdateGeometry", "Wing edge leading | Finished");

                        }

                    }

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Wing edge leading | Finished");
                    }
                }
            }
            else
            {
                // Some reusable values

                // float ctrlOffsetRootLimit = (sharedBaseLength / 2f) / (sharedBaseWidthRoot + sharedEdgeWidthTrailingRoot);
                // float ctrlOffsetTipLimit = (sharedBaseLength / 2f) / (sharedBaseWidthTip + sharedEdgeWidthTrailingTip);

                float ctrlOffsetRootClamped = Mathf.Clamp(isMirrored ? sharedBaseOffsetRoot : -sharedBaseOffsetTip, sharedBaseOffsetLimits.z, sharedBaseOffsetLimits.w + 0.15f) / part.rescaleFactor; // Mathf.Clamp (sharedBaseOffsetRoot, sharedBaseOffsetLimits.z, ctrlOffsetRootLimit - 0.075f);
                float ctrlOffsetTipClamped = Mathf.Clamp(isMirrored ? sharedBaseOffsetTip : -sharedBaseOffsetRoot, Mathf.Max(sharedBaseOffsetLimits.z - 0.15f, ctrlOffsetRootClamped - sharedBaseLength), sharedBaseOffsetLimits.w) / part.rescaleFactor; // Mathf.Clamp (sharedBaseOffsetTip, -ctrlOffsetTipLimit + 0.075f, sharedBaseOffsetLimits.w);

                float ctrlThicknessDeviationRoot = (isMirrored ? sharedBaseThicknessRoot : sharedBaseThicknessTip) / 0.24f / part.rescaleFactor;
                float ctrlThicknessDeviationTip = (isMirrored ? sharedBaseThicknessTip : sharedBaseThicknessRoot) / 0.24f / part.rescaleFactor;

                float ctrlEdgeWidthDeviationRoot = (isMirrored ? sharedEdgeWidthTrailingRoot : sharedEdgeWidthTrailingTip) / 0.24f / part.rescaleFactor;
                float ctrlEdgeWidthDeviationTip = (isMirrored ? sharedEdgeWidthTrailingTip : sharedEdgeWidthTrailingRoot) / 0.24f / part.rescaleFactor;

                float ctrlTipWidth = (isMirrored ? sharedBaseWidthTip : sharedBaseWidthRoot) / part.rescaleFactor;
                float ctrlRootWidth = (isMirrored ? sharedBaseWidthRoot : sharedBaseWidthTip) / part.rescaleFactor;
                // float widthDifference = sharedBaseWidthRoot - sharedBaseWidthTip;
                // float edgeLengthTrailing = Mathf.Sqrt (Mathf.Pow (sharedBaseLength, 2) + Mathf.Pow (widthDifference, 2));
                // float sweepTrailing = 90f - Mathf.Atan (sharedBaseLength / widthDifference) * Mathf.Rad2Deg;

                if (meshFilterCtrlFrame != null)
                {
                    int length = meshReferenceCtrlFrame.vp.Length;
                    Vector3[] vp = new Vector3[length];
                    Array.Copy(meshReferenceCtrlFrame.vp, vp, length);
                    Vector3[] nm = new Vector3[length];
                    Array.Copy(meshReferenceCtrlFrame.nm, nm, length);
                    Vector2[] uv = new Vector2[length];
                    Array.Copy(meshReferenceCtrlFrame.uv, uv, length);
                    Color[] cl = new Color[length];
                    Vector2[] uv2 = new Vector2[length];

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Control surface frame | Passed array setup");
                    }

                    for (int i = 0; i < vp.Length; ++i)
                    {
                        // Thickness correction (X), edge width correction (Y) and span-based offset (Z)
                        vp[i] = vp[i].z < 0f
                            ? new Vector3(vp[i].x * ctrlThicknessDeviationTip, vp[i].y, vp[i].z + 0.5f - geometricLength / 2f)
                            : new Vector3(vp[i].x * ctrlThicknessDeviationRoot, vp[i].y, vp[i].z - 0.5f + geometricLength / 2f);

                        // Left/right sides
                        if (nm[i] == new Vector3(0f, 0f, 1f) || nm[i] == new Vector3(0f, 0f, -1f))
                        {
                            // Filtering out trailing edge cross sections
                            if (uv[i].y > 0.185f)
                            {
                                // Filtering out root neighbours
                                if (vp[i].y < -0.01f)
                                {
                                    if (vp[i].z < 0f)
                                    {
                                        vp[i] = new Vector3(vp[i].x, -ctrlTipWidth, vp[i].z);
                                        uv[i] = new Vector2(ctrlTipWidth, uv[i].y);
                                    }
                                    else
                                    {
                                        vp[i] = new Vector3(vp[i].x, -ctrlRootWidth, vp[i].z);
                                        uv[i] = new Vector2(ctrlRootWidth, uv[i].y);
                                    }
                                }
                            }
                        }
                        // Root (only needs UV adjustment)
                        else if (nm[i] == new Vector3(0f, 1f, 0f) && vp[i].z < 0f)
                        {
                            uv[i] = new Vector2(geometricLength, uv[i].y);
                        }
                        // Trailing edge
                        else if (vp[i].y < -0.1f)
                        {
                            vp[i] = vp[i].z < 0f
                                ? new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlTipWidth, vp[i].z)
                                : new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlRootWidth, vp[i].z);
                        }

                        // Offset-based distortion
                        if (vp[i].z < 0f)
                        {
                            vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetTipClamped);
                            if (nm[i] != new Vector3(0f, 0f, 1f) && nm[i] != new Vector3(0f, 0f, -1f))
                            {
                                uv[i] = new Vector2(uv[i].x - (vp[i].y * ctrlOffsetTipClamped) / 4f, uv[i].y);
                            }
                        }
                        else
                        {
                            vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetRootClamped);
                            if (nm[i] != new Vector3(0f, 0f, 1f) && nm[i] != new Vector3(0f, 0f, -1f))
                            {
                                uv[i] = new Vector2(uv[i].x - (vp[i].y * ctrlOffsetRootClamped) / 4f, uv[i].y);
                            }
                        }

                        // Just blanks
                        cl[i] = new Color(0f, 0f, 0f, 0f);
                        uv2[i] = Vector2.zero;
                    }

                    meshFilterCtrlFrame.mesh.vertices = vp;
                    meshFilterCtrlFrame.mesh.uv = uv;
                    meshFilterCtrlFrame.mesh.uv2 = uv2;
                    meshFilterCtrlFrame.mesh.colors = cl;
                    meshFilterCtrlFrame.mesh.RecalculateBounds();

                    MeshCollider meshCollider = meshFilterCtrlFrame.gameObject.GetComponent<MeshCollider>();
                    if (meshCollider == null)
                    {
                        meshCollider = meshFilterCtrlFrame.gameObject.AddComponent<MeshCollider>();
                    }

                    meshCollider.sharedMesh = null;
                    meshCollider.sharedMesh = meshFilterCtrlFrame.mesh;
                    meshCollider.convex = true;
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Control surface frame | Finished");
                    }
                }

                // Next, time for edge types
                // Before modifying geometry, we have to show the correct objects for the current selection
                // As UI only works with floats, we have to cast selections into ints too

                int ctrlEdgeTypeInt = Mathf.RoundToInt(sharedEdgeTypeTrailing - 1);
                for (int i = 0; i < meshTypeCountEdgeCtrl; ++i)
                {
                    meshFiltersCtrlEdge[i].gameObject.SetActive(i == ctrlEdgeTypeInt);
                }

                // Now we can modify geometry
                // Copy-pasted frame deformation sequence at the moment, to be pruned later

                // Geometry must be modified for all meshes regardless of whether they're active or not
                for (int j = 0; j < meshTypeCountEdgeCtrl; j++)
                {
                    if (meshFiltersCtrlEdge[j] != null)
                    {
                        MeshReference meshReference = meshReferencesCtrlEdge[j];
                        int length = meshReference.vp.Length;
                        Vector3[] vp = new Vector3[length];
                        Array.Copy(meshReference.vp, vp, length);
                        Vector3[] nm = new Vector3[length];
                        Array.Copy(meshReference.nm, nm, length);
                        Vector2[] uv = new Vector2[length];
                        Array.Copy(meshReference.uv, uv, length);
                        Color[] cl = new Color[length];
                        Vector2[] uv2 = new Vector2[length];

                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                        {
                            DebugLogWithID("UpdateGeometry", "Control surface edge | Passed array setup");
                        }

                        for (int i = 0; i < vp.Length; ++i)
                        {
                            // Thickness correction (X), edge width correction (Y) and span-based offset (Z)
                            vp[i] = vp[i].z < 0f
                                ? new Vector3(vp[i].x * ctrlThicknessDeviationTip, ((vp[i].y + 0.5f) * ctrlEdgeWidthDeviationTip) - 0.5f, vp[i].z + 0.5f - geometricLength / 2f)
                                : new Vector3(vp[i].x * ctrlThicknessDeviationRoot, ((vp[i].y + 0.5f) * ctrlEdgeWidthDeviationRoot) - 0.5f, vp[i].z - 0.5f + geometricLength / 2f);

                            // Left/right sides
                            if (nm[i] == new Vector3(0f, 0f, 1f) || nm[i] == new Vector3(0f, 0f, -1f))
                            {
                                vp[i] = vp[i].z < 0f
                                    ? new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlTipWidth, vp[i].z)
                                    : new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlRootWidth, vp[i].z);
                            }

                            // Trailing edge
                            else
                            {
                                // Filtering out root neighbours
                                if (vp[i].y < -0.1f)
                                {
                                    vp[i] = vp[i].z < 0f
                                        ? new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlTipWidth, vp[i].z)
                                        : new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlRootWidth, vp[i].z);
                                }
                            }

                            // Offset-based distortion
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetTipClamped);
                                if (nm[i] != new Vector3(0f, 0f, 1f) && nm[i] != new Vector3(0f, 0f, -1f))
                                {
                                    uv[i] = new Vector2(uv[i].x - (vp[i].y * ctrlOffsetTipClamped) / 4f, uv[i].y);
                                }
                            }
                            else
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetRootClamped);
                                if (nm[i] != new Vector3(0f, 0f, 1f) && nm[i] != new Vector3(0f, 0f, -1f))
                                {
                                    uv[i] = new Vector2(uv[i].x - (vp[i].y * ctrlOffsetRootClamped) / 4f, uv[i].y);
                                }
                            }

                            // Trailing edge (UV adjustment, has to be the last as it's based on cumulative vertex positions)
                            if (nm[i] != new Vector3(0f, 1f, 0f) && nm[i] != new Vector3(0f, 0f, 1f) && nm[i] != new Vector3(0f, 0f, -1f) && uv[i].y < 0.3f)
                            {
                                uv[i] = vp[i].z < 0f ? new Vector2(vp[i].z, uv[i].y) : new Vector2(vp[i].z, uv[i].y);

                                // Color has to be applied there to avoid blanking out cross sections
                                cl[i] = GetVertexColor(2);
                                uv2[i] = GetVertexUV2(sharedMaterialET);
                            }
                        }

                        meshFiltersCtrlEdge[j].mesh.vertices = vp;
                        meshFiltersCtrlEdge[j].mesh.uv = uv;
                        meshFiltersCtrlEdge[j].mesh.uv2 = uv2;
                        meshFiltersCtrlEdge[j].mesh.colors = cl;
                        meshFiltersCtrlEdge[j].mesh.RecalculateBounds();
                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                        {
                            DebugLogWithID("UpdateGeometry", "Control surface edge | Finished");
                        }
                    }
                }

                // Finally, simple top/bottom surface changes

                if (meshFilterCtrlSurface != null)
                {
                    int length = meshReferenceCtrlSurface.vp.Length;
                    Vector3[] vp = new Vector3[length];
                    Array.Copy(meshReferenceCtrlSurface.vp, vp, length);
                    Vector2[] uv = new Vector2[length];
                    Array.Copy(meshReferenceCtrlSurface.uv, uv, length);
                    Color[] cl = new Color[length];
                    Vector2[] uv2 = new Vector2[length];

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Control surface top | Passed array setup");
                    }

                    for (int i = 0; i < vp.Length; ++i)
                    {
                        // Span-based shift
                        if (vp[i].z < 0f)
                        {
                            vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z + 0.5f - geometricLength / 2f);
                            uv[i] = new Vector2(0f, uv[i].y);
                        }
                        else
                        {
                            vp[i] = new Vector3(vp[i].x, vp[i].y, vp[i].z - 0.5f + geometricLength / 2f);
                            uv[i] = new Vector2(geometricLength / 4f, uv[i].y);
                        }

                        // Width-based shift
                        if (vp[i].y < -0.1f)
                        {
                            if (vp[i].z < 0f)
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlTipWidth, vp[i].z);
                                uv[i] = new Vector2(uv[i].x, ctrlTipWidth / 4f);
                            }
                            else
                            {
                                vp[i] = new Vector3(vp[i].x, vp[i].y + 0.5f - ctrlRootWidth, vp[i].z);
                                uv[i] = new Vector2(uv[i].x, ctrlRootWidth / 4f);
                            }
                        }
                        else
                        {
                            uv[i] = new Vector2(uv[i].x, 0f);
                        }

                        // Offsets & thickness
                        if (vp[i].z < 0f)
                        {
                            vp[i] = new Vector3(vp[i].x * ctrlThicknessDeviationTip, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetTipClamped);
                            uv[i] = new Vector2(uv[i].x + (vp[i].y * ctrlOffsetTipClamped) / 4f, uv[i].y);
                        }
                        else
                        {
                            vp[i] = new Vector3(vp[i].x * ctrlThicknessDeviationRoot, vp[i].y, vp[i].z + vp[i].y * ctrlOffsetRootClamped);
                            uv[i] = new Vector2(uv[i].x + (vp[i].y * ctrlOffsetRootClamped) / 4f, uv[i].y);
                        }

                        // Colors
                        if (vp[i].x > 0f)
                        {
                            cl[i] = GetVertexColor(0);
                            uv2[i] = GetVertexUV2(sharedMaterialST);
                        }
                        else
                        {
                            cl[i] = GetVertexColor(1);
                            uv2[i] = GetVertexUV2(sharedMaterialSB);
                        }
                    }
                    meshFilterCtrlSurface.mesh.vertices = vp;
                    meshFilterCtrlSurface.mesh.uv = uv;
                    meshFilterCtrlSurface.mesh.uv2 = uv2;
                    meshFilterCtrlSurface.mesh.colors = cl;
                    meshFilterCtrlSurface.mesh.RecalculateBounds();
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
                    {
                        DebugLogWithID("UpdateGeometry", "Control surface top | Finished");
                    }
                }
            }

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
            {
                DebugLogWithID("UpdateGeometry", "Finished");
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                FuelVolumeChanged();
            }

            if (updateAerodynamics)
            {
                CalculateAerodynamicValues();
                if (aeroIsLiftingSurface)
                    Events["ToggleLiftConfiguration"].guiName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000163");//Surface Config: Lifting
                else
                    Events["ToggleLiftConfiguration"].guiName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000164");//Surface Config: Not Lifting
            }
        }

        public void UpdateCounterparts()
        {
            foreach (Part p in part.symmetryCounterparts)
            {
                WingProcedural clone = FirstOfTypeOrDefault<WingProcedural>(p.Modules);

                clone.sharedArmorRatio = clone.sharedArmorRatioCached = sharedArmorRatio;
                clone.sharedBaseLength = clone.sharedBaseLengthCached = sharedBaseLength;
                clone.sharedBaseWidthRoot = clone.sharedBaseWidthRootCached = sharedBaseWidthRoot;
                clone.sharedBaseWidthTip = clone.sharedBaseWidthTipCached = sharedBaseWidthTip;
                clone.sharedBaseThicknessRoot = clone.sharedBaseThicknessRootCached = sharedBaseThicknessRoot;
                clone.sharedBaseThicknessTip = clone.sharedBaseThicknessTipCached = sharedBaseThicknessTip;
                clone.sharedBaseOffsetRoot = clone.sharedBaseOffsetRootCached = sharedBaseOffsetRoot;
                clone.sharedBaseOffsetTip = clone.sharedBaseOffsetTipCached = sharedBaseOffsetTip;

                clone.sharedEdgeTypeLeading = clone.sharedEdgeTypeLeadingCached = sharedEdgeTypeLeading;
                clone.sharedEdgeWidthLeadingRoot = clone.sharedEdgeWidthLeadingRootCached = sharedEdgeWidthLeadingRoot;
                clone.sharedEdgeWidthLeadingTip = clone.sharedEdgeWidthLeadingTipCached = sharedEdgeWidthLeadingTip;

                clone.sharedEdgeTypeTrailing = clone.sharedEdgeTypeTrailingCached = sharedEdgeTypeTrailing;
                clone.sharedEdgeWidthTrailingRoot = clone.sharedEdgeWidthTrailingRootCached = sharedEdgeWidthTrailingRoot;
                clone.sharedEdgeWidthTrailingTip = clone.sharedEdgeWidthTrailingTipCached = sharedEdgeWidthTrailingTip;

                clone.sharedMaterialST = clone.sharedMaterialSTCached = sharedMaterialST;
                clone.sharedMaterialSB = clone.sharedMaterialSBCached = sharedMaterialSB;
                clone.sharedMaterialET = clone.sharedMaterialETCached = sharedMaterialET;
                clone.sharedMaterialEL = clone.sharedMaterialELCached = sharedMaterialEL;

                clone.sharedColorSTBrightness = clone.sharedColorSTBrightnessCached = sharedColorSTBrightness;
                clone.sharedColorSBBrightness = clone.sharedColorSBBrightnessCached = sharedColorSBBrightness;
                clone.sharedColorETBrightness = clone.sharedColorETBrightnessCached = sharedColorETBrightness;
                clone.sharedColorELBrightness = clone.sharedColorELBrightnessCached = sharedColorELBrightness;

                clone.sharedColorSTOpacity = clone.sharedColorSTOpacityCached = sharedColorSTOpacity;
                clone.sharedColorSBOpacity = clone.sharedColorSBOpacityCached = sharedColorSBOpacity;
                clone.sharedColorETOpacity = clone.sharedColorETOpacityCached = sharedColorETOpacity;
                clone.sharedColorELOpacity = clone.sharedColorELOpacityCached = sharedColorELOpacity;

                clone.sharedColorSTHue = clone.sharedColorSTHueCached = sharedColorSTHue;
                clone.sharedColorSBHue = clone.sharedColorSBHueCached = sharedColorSBHue;
                clone.sharedColorETHue = clone.sharedColorETHueCached = sharedColorETHue;
                clone.sharedColorELHue = clone.sharedColorELHueCached = sharedColorELHue;

                clone.sharedColorSTSaturation = clone.sharedColorSTSaturationCached = sharedColorSTSaturation;
                clone.sharedColorSBSaturation = clone.sharedColorSBSaturationCached = sharedColorSBSaturation;
                clone.sharedColorETSaturation = clone.sharedColorETSaturationCached = sharedColorETSaturation;
                clone.sharedColorELSaturation = clone.sharedColorELSaturationCached = sharedColorELSaturation;

                clone.RefreshGeometry();
            }
        }

        // Edge geometry
        public Vector3[] GetReferenceVertices(MeshFilter source)
        {
            Vector3[] positions = new Vector3[0];
            if (source != null)
            {
                if (source.mesh != null)
                {
                    positions = source.mesh.vertices;
                    return positions;
                }
            }
            return positions;
        }

        #endregion Geometry

        #region Mesh Setup and Checking

        private void SetupMeshFilters()
        {
            if (!isCtrlSrf)
            {
                meshFilterWingSurface = CheckMeshFilter(meshFilterWingSurface, "surface");
                meshFilterWingSection = CheckMeshFilter(meshFilterWingSection, "section");
                for (int i = 0; i < meshTypeCountEdgeWing; ++i)
                {
                    MeshFilter meshFilterWingEdgeTrailing = CheckMeshFilter("edge_trailing_type" + i);
                    meshFiltersWingEdgeTrailing.Add(meshFilterWingEdgeTrailing);

                    MeshFilter meshFilterWingEdgeLeading = CheckMeshFilter("edge_leading_type" + i);
                    meshFiltersWingEdgeLeading.Add(meshFilterWingEdgeLeading);
                }
            }
            else
            {
                meshFilterCtrlFrame = CheckMeshFilter(meshFilterCtrlFrame, "frame");
                meshFilterCtrlSurface = CheckMeshFilter(meshFilterCtrlSurface, "surface");
                for (int i = 0; i < meshTypeCountEdgeCtrl; ++i)
                {
                    MeshFilter meshFilterCtrlEdge = CheckMeshFilter("edge_type" + i);
                    meshFiltersCtrlEdge.Add(meshFilterCtrlEdge);
                }
            }
        }

        public void SetupMeshReferences()
        {
            bool required = true;

            if (!isCtrlSrf)
            {
                if (meshReferenceWingSection != null && meshReferenceWingSurface != null && meshReferencesWingEdge[meshTypeCountEdgeWing - 1] != null)
                {
                    required &= (meshReferenceWingSection.vp.Length <= 0 || meshReferenceWingSurface.vp.Length <= 0 || meshReferencesWingEdge[meshTypeCountEdgeWing - 1].vp.Length <= 0);
                }
            }
            else
            {
                if (meshReferenceCtrlFrame != null && meshReferenceCtrlSurface != null && meshReferencesCtrlEdge[meshTypeCountEdgeCtrl - 1] != null)
                {
                    required &= (meshReferenceCtrlFrame.vp.Length <= 0 || meshReferenceCtrlSurface.vp.Length <= 0 || meshReferencesCtrlEdge[meshTypeCountEdgeCtrl - 1].vp.Length <= 0);
                }
            }

            if (required)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logMeshReferences)
                {
                    DebugLogWithID("SetupMeshReferences", "References missing | isCtrlSrf: " + isCtrlSrf);
                }

                SetupMeshReferencesFromScratch();
            }
            else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logMeshReferences)
            {
                DebugLogWithID("SetupMeshReferences", "Skipped, all references seem to be in order");
            }
        }

        public void ReportOnMeshReferences()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logMeshReferences)
            {
                if (isCtrlSrf)
                {
                    DebugLogWithID("ReportOnMeshReferences", "Control surface reference length check" + " | Edge: " + meshReferenceCtrlFrame.vp.Length
                                        + " | Surface: " + meshReferenceCtrlSurface.vp.Length);
                }
                else
                {
                    DebugLogWithID("ReportOnMeshReferences", "Wing reference length check" + " | Section: " + meshReferenceWingSection.vp.Length
                                        + " | Surface: " + meshReferenceWingSurface.vp.Length);
                }
            }
        }

        private void SetupMeshReferencesFromScratch()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logMeshReferences)
            {
                DebugLogWithID("SetupMeshReferencesFromScratch", "No sources found, creating new references");
            }

            if (!isCtrlSrf)
            {
                meshReferenceWingSection = FillMeshRefererence(meshFilterWingSection);
                meshReferenceWingSurface = FillMeshRefererence(meshFilterWingSurface);
                for (int i = 0; i < meshTypeCountEdgeWing; ++i)
                {
                    MeshReference meshReferenceWingEdge = FillMeshRefererence(meshFiltersWingEdgeTrailing[i]);
                    meshReferencesWingEdge.Add(meshReferenceWingEdge);
                }
            }
            else
            {
                meshReferenceCtrlFrame = FillMeshRefererence(meshFilterCtrlFrame);
                meshReferenceCtrlSurface = FillMeshRefererence(meshFilterCtrlSurface);
                for (int i = 0; i < meshTypeCountEdgeCtrl; ++i)
                {
                    MeshReference meshReferenceCtrlEdge = FillMeshRefererence(meshFiltersCtrlEdge[i]);
                    meshReferencesCtrlEdge.Add(meshReferenceCtrlEdge);
                }
            }
        }

        // Reference fetching

        private MeshFilter CheckMeshFilter(string name)
        {
            return CheckMeshFilter(null, name, false);
        }

        private MeshFilter CheckMeshFilter(MeshFilter reference, string name)
        {
            return CheckMeshFilter(reference, name, false);
        }

        private MeshFilter CheckMeshFilter(MeshFilter reference, string name, bool disable)
        {
            if (reference == null)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCheckMeshFilter)
                {
                    DebugLogWithID("CheckMeshFilter", "Looking for object: " + name);
                }
                Transform parent = part.transform.GetChild(0).GetChild(0).GetChild(0).Find(name);

                if (parent != null)
                {
                    parent.localPosition = Vector3.zero;
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCheckMeshFilter)
                    {
                        DebugLogWithID("CheckMeshFilter", "Object " + name + " was found");
                    }

                    reference = parent.gameObject.GetComponent<MeshFilter>();
                    if (disable)
                    {
                        parent.gameObject.SetActive(false);
                    }
                }
                else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCheckMeshFilter)
                {
                    DebugLogWithID("CheckMeshFilter", "Object " + name + " was not found!");
                }
            }
            return reference;
        }

        private Transform CheckTransform(string name)
        {
            Transform t = part.transform.GetChild(0).GetChild(0).GetChild(0).Find(name);
            return t;
        }

        private MeshReference FillMeshRefererence(MeshFilter source)
        {
            MeshReference reference = new MeshReference();

            if (source != null)
            {
                int length = source.mesh.vertices.Length;
                reference.vp = new Vector3[length];
                Array.Copy(source.mesh.vertices, reference.vp, length);
                reference.nm = new Vector3[length];
                Array.Copy(source.mesh.normals, reference.nm, length);
                reference.uv = new Vector2[length];
                Array.Copy(source.mesh.uv, reference.uv, length);
            }
            else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logMeshReferences)
            {
                DebugLogWithID("FillMeshReference", "Mesh filter reference is null, unable to set up reference arrays");
            }

            return reference;
        }

        private void SetupMirroredCntrlSrf()
        {
            if (assemblyFARUsed) return;

            if (this.isCtrlSrf && part.symMethod == SymmetryMethod.Mirror && part.symmetryCounterparts.Count > 0)
            {
                if (this.part.Modules.Contains<ModuleControlSurface>())
                {
                    ModuleControlSurface m = this.part.Modules.GetModule<ModuleControlSurface>();
                    m.usesMirrorDeploy = true;
                    {
                        Part other = part.symmetryCounterparts[0];
                        m.mirrorDeploy = this.part.transform.position.x > other.transform.position.x;
                        m.partDeployInvert = !m.mirrorDeploy;
                    }
                }
                else
                {
                    Debug.LogError(String.Format("[B9PW] Part [{0}] named [{1}] is a Control Surface but a ModuleControlSurface wasn't found on its module list!", this.part.ClassName, this.part.partName));
                }
            }
        }

        #endregion Mesh Setup and Checking

        #region Materials

        public static Material materialLayeredSurface;
        public static Texture materialLayeredSurfaceTextureMain;
        public static Texture materialLayeredSurfaceTextureMask;

        public static Material materialLayeredEdge;
        public static Texture materialLayeredEdgeTextureMain;
        public static Texture materialLayeredEdgeTextureMask;

        private readonly float materialPropertyShininess = 0.4f;
        private Color materialPropertySpecular = new Color(0.62109375f, 0.62109375f, 0.62109375f, 1.0f);

        public void UpdateMaterials()
        {
            if (materialLayeredSurface == null || materialLayeredEdge == null)
            {
                SetMaterialReferences();
            }

            if (materialLayeredSurface != null)
            {
                if (!isCtrlSrf)
                {
                    SetMaterial(meshFilterWingSurface, materialLayeredSurface);
                    for (int i = 0; i < meshTypeCountEdgeWing; ++i)
                    {
                        SetMaterial(meshFiltersWingEdgeTrailing[i], materialLayeredEdge);
                        SetMaterial(meshFiltersWingEdgeLeading[i], materialLayeredEdge);
                    }
                }
                else
                {
                    SetMaterial(meshFilterCtrlSurface, materialLayeredSurface);
                    SetMaterial(meshFilterCtrlFrame, materialLayeredEdge);
                    for (int i = 0; i < meshTypeCountEdgeCtrl; ++i)
                    {
                        SetMaterial(meshFiltersCtrlEdge[i], materialLayeredEdge);
                    }
                }
            }
            else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateMaterials)
            {
                DebugLogWithID("UpdateMaterials", "Material creation failed");
            }
        }

        private void SetMaterialReferences()
        {
            if (materialLayeredSurface == null)
            {
                materialLayeredSurface = new Material(StaticWingGlobals.wingShader);
            }

            if (materialLayeredEdge == null)
            {
                materialLayeredEdge = new Material(StaticWingGlobals.wingShader);
            }

            if (!isCtrlSrf)
            {
                SetTextures(meshFilterWingSurface, meshFiltersWingEdgeTrailing[0]);
            }
            else
            {
                SetTextures(meshFilterCtrlSurface, meshFilterCtrlFrame);
            }

            if (materialLayeredSurfaceTextureMain != null && materialLayeredSurfaceTextureMask != null)
            {
                materialLayeredSurface.SetTexture("_MainTex", materialLayeredSurfaceTextureMain);
                materialLayeredSurface.SetTexture("_Emissive", materialLayeredSurfaceTextureMask);
                materialLayeredSurface.SetFloat("_Shininess", materialPropertyShininess);
                materialLayeredSurface.SetColor("_SpecColor", materialPropertySpecular);
            }
            else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateMaterials)
            {
                DebugLogWithID("SetMaterialReferences", "Surface textures not found");
            }

            if (materialLayeredEdgeTextureMain != null && materialLayeredEdgeTextureMask != null)
            {
                materialLayeredEdge.SetTexture("_MainTex", materialLayeredEdgeTextureMain);
                materialLayeredEdge.SetTexture("_Emissive", materialLayeredEdgeTextureMask);
                materialLayeredEdge.SetFloat("_Shininess", materialPropertyShininess);
                materialLayeredEdge.SetColor("_SpecColor", materialPropertySpecular);
            }
            else if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateMaterials)
            {
                DebugLogWithID("SetMaterialReferences", "Edge textures not found");
            }
        }

        private void SetMaterial(MeshFilter target, Material material)
        {
            if (target != null)
            {
                Renderer r = target.gameObject.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = material;
                }
            }
        }

        private void SetTextures(MeshFilter sourceSurface, MeshFilter sourceEdge)
        {
            if (sourceSurface != null)
            {
                Renderer r = sourceSurface.gameObject.GetComponent<Renderer>();
                if (r != null)
                {
                    materialLayeredSurfaceTextureMain = r.sharedMaterial.GetTexture("_MainTex");
                    materialLayeredSurfaceTextureMask = r.sharedMaterial.GetTexture("_Emissive");
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateMaterials)
                    {
                        DebugLogWithID("SetTextures", "Main: " + materialLayeredSurfaceTextureMain.ToString() + " | Mask: " + materialLayeredSurfaceTextureMask);
                    }
                }
            }

            if (sourceEdge != null)
            {
                Renderer r = sourceEdge.gameObject.GetComponent<Renderer>();
                if (r != null)
                {
                    materialLayeredEdgeTextureMain = r.sharedMaterial.GetTexture("_MainTex");
                    materialLayeredEdgeTextureMask = r.sharedMaterial.GetTexture("_Emissive");
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateMaterials)
                    {
                        DebugLogWithID("SetTextures", "Main: " + materialLayeredEdgeTextureMain.ToString() + " | Mask: " + materialLayeredEdgeTextureMask);
                    }
                }
            }
        }

        #endregion Materials

        #region Aero

        public class VesselStatus
        {
            public Vessel vessel = null;
            public bool isUpdated = false;

            public VesselStatus(Vessel v, bool state)
            {
                vessel = v;
                isUpdated = state;
            }
        }

        public static List<VesselStatus> vesselList = new List<VesselStatus>();

        // Delayed aero value setup
        // Must be run after all geometry setups, otherwise FAR checks will be done before surrounding parts take shape, producing incorrect results
        public IEnumerator SetupReorderedForFlight()
        {
            // First we need to determine whether the vessel this part is attached to is included into the status list
            // If it's included, we need to fetch it's index in that list

            bool vesselListInclusive = false;
            int vesselID = vessel.GetInstanceID();
            int vesselStatusIndex = 0;
            int vesselListCount = vesselList.Count;

            for (int i = 0; i < vesselListCount; ++i)
            {
                if (vesselList[i].vessel.GetInstanceID() == vesselID)
                {
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFlightSetup)
                    {
                        DebugLogWithID("SetupReorderedForFlight", "Vessel " + vesselID + " found in the status list");
                    }

                    vesselListInclusive = true;
                    vesselStatusIndex = i;
                }
            }

            // If it was not included, we add it to the list
            // Correct index is then fairly obvious

            if (!vesselListInclusive)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFlightSetup)
                {
                    DebugLogWithID("SetupReorderedForFlight", "Vessel " + vesselID + " was not found in the status list, adding it");
                }

                vesselList.Add(new VesselStatus(vessel, false));
                vesselStatusIndex = vesselList.Count - 1;
            }

            // Using the index for the status list we obtained, we check whether it was updated yet
            // So that only one part can run the following part

            if (!vesselList[vesselStatusIndex].isUpdated)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFlightSetup)
                {
                    DebugLogWithID("SetupReorderedForFlight", "Vessel " + vesselID + " was not updated yet (this message should only appear once)");
                }

                vesselList[vesselStatusIndex].isUpdated = true;
                List<WingProcedural> moduleList = new List<WingProcedural>();

                // First we get a list of all relevant parts in the vessel
                // Found modules are added to a list

                int vesselPartsCount = vessel.parts.Count;
                for (int i = 0; i < vesselPartsCount; ++i)
                {
                    if (vessel.parts[i].Modules.Contains<WingProcedural>())
                    {
                        moduleList.Add(vessel.parts[i].Modules.GetModule<WingProcedural>());
                    }
                }

                // After that we make two separate runs through that list
                // First one setting up all geometry and second one setting up aerodynamic values

                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFlightSetup)
                {
                    DebugLogWithID("SetupReorderedForFlight", "Vessel " + vesselID + " contained " + vesselPartsCount + " parts, of which " + moduleList.Count + " should be set up");
                }

                int moduleListCount = moduleList.Count;
                for (int i = 0; i < moduleListCount; ++i)
                {
                    moduleList[i].Setup();
                }

                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFlightSetup)
                {
                    DebugLogWithID("SetupReorderedForFlight", "Vessel " + vesselID + " waited for updates, starting aero value calculation");
                }

                for (int i = 0; i < moduleListCount; ++i)
                {
                    moduleList[i].CalculateAerodynamicValues();
                }
            }
        }

        // Aerodynamics value calculation
        // More or less lifted from pWings, so credit goes to DYJ and Taverius

        [KSPField]
        public float aeroConstLiftFudgeNumber = 0.0775f;

        [KSPField]
        public float aeroConstMassFudgeNumber = 0.015f;

        [KSPField]
        public float aeroConstDragBaseValue = 0.6f;

        [KSPField]
        public float aeroConstDragMultiplier = 3.3939f;

        [KSPField]
        public float aeroConstConnectionFactor = 150f;

        [KSPField]
        public float aeroConstConnectionMinimum = 50f;

        [KSPField]
        public float aeroConstCostDensity = 5300f;

        [KSPField]
        public float aeroConstCostDensityControl = 6500f;

        [KSPField]
        public float aeroConstControlSurfaceFraction = 1f;

        public float aeroUICost;
        public float aeroStatVolume = 3.84f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000125")]		// #autoLOC_B9_Aerospace_WingStuff_1000125 = Mass
        public float aeroUIMass;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000126")]		// #autoLOC_B9_Aerospace_WingStuff_1000126 = Stock lifting area
        public float stockLiftCoefficient;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Is Lifting Surface", guiFormat = "S4")]
        public bool aeroIsLiftingSurface = true;

        public double aeroStatCd;
        public double aeroStatCl;
        public double aeroStatClChildren;
        public double aeroStatMass;
        public double aeroStatConnectionForce;

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000127")]		// #autoLOC_B9_Aerospace_WingStuff_1000127 = MAC
        public double aeroStatMeanAerodynamicChord;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000128")]		// #autoLOC_B9_Aerospace_WingStuff_1000128 = Semispan
        public double aeroStatSemispan;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000129")]		// #autoLOC_B9_Aerospace_WingStuff_1000129 = Mid Chord Sweep
        public double aeroStatMidChordSweep;
        public Vector3d aeroStatRootMidChordOffsetFromOrigin;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#autoLOC_B9_Aerospace_WingStuff_1000130")]		// #autoLOC_B9_Aerospace_WingStuff_1000130 = Taper Ratio
        public double aeroStatTaperRatio;
        public double aeroStatSurfaceArea;
        public double aeroStatAspectRatio;
        public double aeroStatAspectRatioSweepScale;

        private PartModule aeroFARModuleReference;
        private Type aeroFARModuleType;

        private FieldInfo aeroFARFieldInfoSemispan;
        private FieldInfo aeroFARFieldInfoSemispan_Actual; // to handle tweakscale, wings have semispan (unscaled) and semispan_actual (tweakscaled). Need to set both (actual is the important one, and tweakscale isn't needed here, so only _actual actually needs to be set, but it would be silly to not set it)
        private FieldInfo aeroFARFieldInfoMAC;
        private FieldInfo aeroFARFieldInfoMAC_Actual; //  to handle tweakscale, wings have MAC (unscaled) and MAC_actual (tweakscaled). Need to set both (actual is the important one, and tweakscale isn't needed here, so only _actual actually needs to be set, but it would be silly to not set it)
        private FieldInfo aeroFARFieldInfoSurfaceArea; // calculated internally from b_2_actual and MAC_actual
        private FieldInfo aeroFARFieldInfoMidChordSweep;
        private FieldInfo aeroFARFieldInfoTaperRatio;
        private FieldInfo aeroFARFieldInfoControlSurfaceFraction;
        private FieldInfo aeroFARFieldInfoRootChordOffset;
        private MethodInfo aeroFARMethodInfoUsed;

        public void CalculateAerodynamicValues()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
            {
                DebugLogWithID("CalculateAerodynamicValues", "Started");
            }

            float sharedWidthTipSum = sharedBaseWidthTip;
            float sharedWidthRootSum = sharedBaseWidthRoot;

            if (!isCtrlSrf)
            {
                double offset = 0;

                if (sharedEdgeTypeLeading != 1)
                {
                    sharedWidthTipSum += sharedEdgeWidthLeadingTip;
                    sharedWidthRootSum += sharedEdgeWidthLeadingRoot;
                    offset += 0.2 * (sharedEdgeWidthLeadingRoot + sharedEdgeWidthLeadingTip);
                }

                if (sharedEdgeTypeTrailing != 1)
                {
                    sharedWidthTipSum += sharedEdgeWidthTrailingTip;
                    sharedWidthRootSum += sharedEdgeWidthTrailingRoot;
                    offset -= 0.25 * (sharedEdgeWidthTrailingRoot + sharedEdgeWidthTrailingTip);
                }
                aeroStatRootMidChordOffsetFromOrigin = offset * Vector3d.up;
            }
            else
            {
                sharedWidthTipSum += sharedEdgeWidthTrailingTip;
                sharedWidthRootSum += sharedEdgeWidthTrailingRoot;
            }

            float ctrlOffsetRootLimit = (sharedBaseLength / 2f) / (sharedBaseWidthRoot + sharedEdgeWidthTrailingRoot);
            float ctrlOffsetTipLimit = (sharedBaseLength / 2f) / (sharedBaseWidthTip + sharedEdgeWidthTrailingTip);

            float ctrlOffsetRootClamped = Mathf.Clamp(sharedBaseOffsetRoot, -ctrlOffsetRootLimit, ctrlOffsetRootLimit);
            float ctrlOffsetTipClamped = Mathf.Clamp(sharedBaseOffsetTip, -ctrlOffsetTipLimit, ctrlOffsetTipLimit);

            // quadratic equation to get ratio in wich to divide wing to get equal areas
            // tip      - wigtip width
            // 1 - x
            // h
            // x        - ratio in question
            // base     - base width

            // h = base + x * (tip - base)
            // (tip + h) * (1 - x) = (base + h) * x     - aera equality
            // tip + h - x * tip - h * x = base * x + h * x
            // 2 * h * x + x * (base + tip) - tip - h = 0
            // 2 * (base + x * (tip - base)) * x + x * (base + tip) - tip - base - x * (tip - base) = 0
            // x^2 * 2 * (tip - base) + x * (2 * base + base + tip - (tip - base)) - tip - base = 0
            // x^2 * 2 * (tip - base) + x * 4 * base - tip - base = 0
            float a_tp = 2.0f * (sharedBaseWidthTip - sharedBaseWidthRoot);
            float pseudotaper_ratio;
            if (a_tp != 0.0f)
            {
                float b_tp = 4.0f * sharedBaseWidthRoot;
                float c_tp = -sharedBaseWidthTip - sharedBaseWidthRoot;
                float D_tp = b_tp * b_tp - 4.0f * a_tp * c_tp;
                float x1 = (-b_tp + Mathf.Sqrt(D_tp)) / 2.0f / a_tp;
                float x2 = (-b_tp - Mathf.Sqrt(D_tp)) / 2.0f / a_tp;
                pseudotaper_ratio = (x1 >= 0.0f) && (x1 <= 1.0f) ? x1 : x2;
            }
            else
            {
                pseudotaper_ratio = 0.5f;
            }

            // Base four values

            if (!isCtrlSrf)
            {
                aeroStatSemispan = (double)sharedBaseLength;
                aeroStatTaperRatio = (double)sharedWidthTipSum / (double)sharedWidthRootSum;
                aeroStatMeanAerodynamicChord = (double)(sharedWidthTipSum + sharedWidthRootSum) / 2.0;
                aeroStatMidChordSweep = Math.Atan((double)sharedBaseOffsetTip / (double)sharedBaseLength) * Mathf.Rad2Deg;
            }
            else
            {
                aeroStatSemispan = (double)sharedBaseLength;
                aeroStatTaperRatio = (double)(sharedBaseLength + sharedWidthTipSum * ctrlOffsetTipClamped - sharedWidthRootSum * ctrlOffsetRootClamped) / (double)sharedBaseLength;
                aeroStatMeanAerodynamicChord = (double)(sharedWidthTipSum + sharedWidthRootSum) / 2.0;
                aeroStatMidChordSweep = Math.Atan((double)Mathf.Abs(sharedWidthRootSum - sharedWidthTipSum) / (double)sharedBaseLength) * Mathf.Rad2Deg;
            }

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
            {
                DebugLogWithID("CalculateAerodynamicValues", "Passed B2/TR/MAC/MCS");
            }

            // Derived values

            aeroStatSurfaceArea = aeroStatMeanAerodynamicChord * aeroStatSemispan;
            aeroStatAspectRatio = 2.0f * aeroStatSemispan / aeroStatMeanAerodynamicChord;

            aeroStatAspectRatioSweepScale = Math.Pow(aeroStatAspectRatio / Math.Cos(Mathf.Deg2Rad * aeroStatMidChordSweep), 2.0f) + 4.0f;
            aeroStatAspectRatioSweepScale = 2.0f + Math.Sqrt(aeroStatAspectRatioSweepScale);
            aeroStatAspectRatioSweepScale = (2.0f * Math.PI) / aeroStatAspectRatioSweepScale * aeroStatAspectRatio;

            aeroStatMass = MathD.Clamp(aeroConstMassFudgeNumber * aeroStatSurfaceArea * ((aeroStatAspectRatioSweepScale * 2.0) / (3.0 + aeroStatAspectRatioSweepScale)) * ((1.0 + aeroStatTaperRatio) / 2), 0.01, double.MaxValue);
            aeroStatCd = aeroConstDragBaseValue / aeroStatAspectRatioSweepScale * aeroConstDragMultiplier;
            aeroStatCl = aeroConstLiftFudgeNumber * aeroStatSurfaceArea * aeroStatAspectRatioSweepScale;
            GatherChildrenCl();
            aeroStatConnectionForce = Math.Round(MathD.Clamp(Math.Sqrt(aeroStatCl + aeroStatClChildren) * (double)aeroConstConnectionFactor, (double)aeroConstConnectionMinimum, double.MaxValue));

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
            {
                DebugLogWithID("CalculateAerodynamicValues", "Passed SR/AR/ARSS/mass/Cl/Cd/connection");
            }

            // Shared parameters

            if (!isCtrlSrf)
            {
                aeroUICost = (float)aeroStatMass * (1f + (float)aeroStatAspectRatioSweepScale / 4f) * aeroConstCostDensity;
                aeroUICost = Mathf.Round(aeroUICost / 5f) * 5f;
                part.CoMOffset = part.CoLOffset = part.CoPOffset = new Vector3(sharedBaseLength * pseudotaper_ratio, -sharedBaseOffsetTip * pseudotaper_ratio, 0f);
            }
            else
            {
                aeroUICost = (float)aeroStatMass * (1f + (float)aeroStatAspectRatioSweepScale / 4f) * aeroConstCostDensity * (1f - aeroConstControlSurfaceFraction);
                aeroUICost += (float)aeroStatMass * (1f + (float)aeroStatAspectRatioSweepScale / 4f) * aeroConstCostDensityControl * aeroConstControlSurfaceFraction;
                aeroUICost = Mathf.Round(aeroUICost / 5f) * 5f;
                part.CoMOffset = part.CoLOffset = part.CoPOffset = new Vector3(0f, -(sharedWidthRootSum + sharedWidthTipSum) / 4f, 0f);
            }
            aeroUICost -= part.partInfo.cost; // it's additional cost

            part.breakingForce = Mathf.Round((float)aeroStatConnectionForce);
            part.breakingTorque = Mathf.Round((float)aeroStatConnectionForce);

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
            {
                DebugLogWithID("CalculateAerodynamicValues", "Passed cost/force/torque");
            }

            // Stock-only values
            if (!assemblyFARUsed)
            {
                float stockLiftCoeff = (float)aeroStatSurfaceArea / 3.52f;
                stockLiftCoefficient = aeroIsLiftingSurface ? stockLiftCoeff : 0f;
                float x_col = pseudotaper_ratio * sharedBaseOffsetTip;
                float y_col = pseudotaper_ratio * sharedBaseLength;

                if (!isCtrlSrf && !isWingAsCtrlSrf)
                {
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                    {
                        DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR is inactive, calculating values for winglet part type");
                    }

                    part.Modules.GetModule<ModuleLiftingSurface>().deflectionLiftCoeff = (float)Math.Round(stockLiftCoefficient, 2);
                    aeroUIMass = stockLiftCoeff * 0.1f;
                    part.CoLOffset = new Vector3(y_col, -x_col, 0.0f);
                }
                else
                {
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                    {
                        DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR is inactive, calculating stock control surface module values");
                    }

                    ModuleControlSurface mCtrlSrf = FirstOfTypeOrDefault<ModuleControlSurface>(part.Modules);
                    mCtrlSrf.deflectionLiftCoeff = (float)Math.Round(stockLiftCoefficient, 2);
                    mCtrlSrf.ctrlSurfaceArea = aeroConstControlSurfaceFraction;
                    aeroUIMass = stockLiftCoeff * (1 + mCtrlSrf.ctrlSurfaceArea) * 0.1f;
                    part.CoLOffset = isWingAsCtrlSrf
                        ? new Vector3(y_col, -x_col, 0.0f)
                        : new Vector3(y_col - 0.5f * sharedBaseLength, -0.25f * (sharedBaseWidthTip + sharedBaseWidthRoot), 0.0f);
                }

                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                {
                    DebugLogWithID("CalculateAerodynamicValues", "Passed stock drag/deflection/area");
                }
            }
            else
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                {
                    DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Entered segment");
                }

                if (aeroFARModuleReference == null)
                {
                    if (part.Modules.Contains("FARControllableSurface"))
                    {
                        aeroFARModuleReference = part.Modules["FARControllableSurface"];
                    }
                    else if (part.Modules.Contains("FARWingAerodynamicModel"))
                    {
                        aeroFARModuleReference = part.Modules["FARWingAerodynamicModel"];
                    }

                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                    {
                        DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Module reference was null, search performed, recheck result was " + (aeroFARModuleReference == null).ToString());
                    }
                }

                if (aeroFARModuleReference != null)
                {
                    if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                    {
                        DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Module reference present");
                    }

                    if (aeroFARModuleType == null)
                    {
                        aeroFARModuleType = aeroFARModuleReference.GetType();
                    }

                    if (aeroFARModuleType != null)
                    {
                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                        {
                            DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Module type present");
                        }

                        if (aeroFARFieldInfoSemispan == null)
                        {
                            aeroFARFieldInfoSemispan = aeroFARModuleType.GetField("b_2");
                            aeroFARFieldInfoSemispan_Actual = aeroFARModuleType.GetField("b_2_actual");
                            aeroFARFieldInfoMAC = aeroFARModuleType.GetField("MAC");
                            aeroFARFieldInfoMAC_Actual = aeroFARModuleType.GetField("MAC_actual");
                            aeroFARFieldInfoSurfaceArea = aeroFARModuleType.GetField("S");
                            aeroFARFieldInfoMidChordSweep = aeroFARModuleType.GetField("MidChordSweep");
                            aeroFARFieldInfoTaperRatio = aeroFARModuleType.GetField("TaperRatio");
                        }

                        if (isCtrlSrf)
                        {
                            if (aeroFARFieldInfoControlSurfaceFraction == null)
                            {
                                aeroFARFieldInfoControlSurfaceFraction = aeroFARModuleType.GetField("ctrlSurfFrac");
                            }
                        }
                        else
                        {
                            if (aeroFARFieldInfoRootChordOffset == null)
                            {
                                aeroFARFieldInfoRootChordOffset = aeroFARModuleType.GetField("rootMidChordOffsetFromOrig");
                            }
                        }

                        if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                        {
                            DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Field checks and fetching passed");
                        }

                        if (aeroFARMethodInfoUsed == null)
                        {
                            aeroFARMethodInfoUsed = aeroFARModuleType.GetMethod("StartInitialization");
                            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                            {
                                DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Method info was null, search performed, recheck result was " + (aeroFARMethodInfoUsed == null).ToString());
                            }
                        }

                        if (aeroFARMethodInfoUsed != null)
                        {
                            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                            {
                                DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Method info present");
                            }

                            aeroFARFieldInfoSemispan.SetValue(aeroFARModuleReference, aeroStatSemispan);
                            aeroFARFieldInfoSemispan_Actual.SetValue(aeroFARModuleReference, aeroStatSemispan);
                            aeroFARFieldInfoMAC.SetValue(aeroFARModuleReference, aeroStatMeanAerodynamicChord);
                            aeroFARFieldInfoMAC_Actual.SetValue(aeroFARModuleReference, aeroStatMeanAerodynamicChord);
                            //aeroFARFieldInfoSurfaceArea.SetValue (aeroFARModuleReference, aeroStatSurfaceArea);
                            aeroFARFieldInfoMidChordSweep.SetValue(aeroFARModuleReference, aeroStatMidChordSweep);
                            aeroFARFieldInfoTaperRatio.SetValue(aeroFARModuleReference, aeroStatTaperRatio);

                            if (isCtrlSrf)
                            {
                                aeroFARFieldInfoControlSurfaceFraction.SetValue(aeroFARModuleReference, aeroConstControlSurfaceFraction);
                            }
                            else
                            {
                                aeroFARFieldInfoRootChordOffset.SetValue(aeroFARModuleReference, (Vector3)aeroStatRootMidChordOffsetFromOrigin);
                            }

                            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                            {
                                DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | All values set, invoking the method");
                            }

                            aeroFARMethodInfoUsed.Invoke(aeroFARModuleReference, null);
                        }
                    }
                }

                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
                {
                    DebugLogWithID("CalculateAerodynamicValues", "FAR/NEAR | Segment ended");
                }
            }

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logCAV)
            {
                DebugLogWithID("CalculateAerodynamicValues", "Finished");
            }

            StartCoroutine(UpdateAeroDelayed());
        }

        private float updateTimeDelay = 0;
        private IEnumerator UpdateAeroDelayed()
        {
            bool running = updateTimeDelay > 0;
            updateTimeDelay = 0.5f;

            if (running)
            {
                yield break;
            }

            while (updateTimeDelay > 0)
            {
                updateTimeDelay -= TimeWarp.deltaTime;
                yield return null;
            }

            if (assemblyFARUsed)
            {
                if (part.Modules.Contains("FARWingAerodynamicModel"))
                {
                    PartModule FARmodule = part.Modules["FARWingAerodynamicModel"];
                    Type FARtype = FARmodule.GetType();
                    FARtype.GetMethod("StartInitialization").Invoke(FARmodule, null);
                }
                part.SendMessage("GeometryPartModuleRebuildMeshData"); // notify FAR that geometry has changed
            }
            else
            {
                DragCube DragCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
                part.DragCubes.ClearCubes();
                part.DragCubes.Cubes.Add(DragCube);
                part.DragCubes.ResetCubeWeights();
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }

            updateTimeDelay = 0;
        }

        public void GatherChildrenCl()
        {
            aeroStatClChildren = 0;

            // Add up the Cl and ChildrenCl of all our children to our ChildrenCl
            foreach (Part p in part.children)
            {
                if (p == null)
                {
                    continue;
                }

                WingProcedural child = FirstOfTypeOrDefault<WingProcedural>(p.Modules);
                if (child == null)
                {
                    continue;
                }

                aeroStatClChildren += child.aeroStatCl;
                aeroStatClChildren += child.aeroStatClChildren;
            }

            // If parent is a pWing, trickle the call to gather ChildrenCl up to them.
            if (part.parent != null && part.parent.Modules.Contains<WingProcedural>())
            {
                FirstOfTypeOrDefault<WingProcedural>(part.parent.Modules).GatherChildrenCl();
            }
        }

        // [KSPEvent (guiActive = true, guiActiveEditor = true, guiName = "Dump interaction data")]
        public void DumpInteractionData()
        {
            if (part.Modules.Contains("FARWingAerodynamicModel"))
            {
                PartModule moduleFAR = part.Modules["FARWingAerodynamicModel"];
                Type typeFAR = moduleFAR.GetType();

                object referenceInteraction = typeFAR.GetField("wingInteraction", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(moduleFAR);
                if (referenceInteraction != null)
                {
                    string report = "";
                    Type typeInteraction = referenceInteraction.GetType();
                    Type runtimeListType = typeof(List<>).MakeGenericType(typeFAR);

                    FieldInfo forwardExposureInfo = typeInteraction.GetField("forwardExposure", BindingFlags.NonPublic | BindingFlags.Instance);
                    double forwardExposure = (double)forwardExposureInfo.GetValue(referenceInteraction);
                    FieldInfo backwardExposureInfo = typeInteraction.GetField("backwardExposure", BindingFlags.NonPublic | BindingFlags.Instance);
                    double backwardExposure = (double)backwardExposureInfo.GetValue(referenceInteraction);
                    FieldInfo leftwardExposureInfo = typeInteraction.GetField("leftwardExposure", BindingFlags.NonPublic | BindingFlags.Instance);
                    double leftwardExposure = (double)leftwardExposureInfo.GetValue(referenceInteraction);
                    FieldInfo rightwardExposureInfo = typeInteraction.GetField("rightwardExposure", BindingFlags.NonPublic | BindingFlags.Instance);
                    double rightwardExposure = (double)rightwardExposureInfo.GetValue(referenceInteraction);
                    report += "Exposure (fwd/back/left/right): " + forwardExposure.ToString("F2") + ", " + backwardExposure.ToString("F2") + ", " + leftwardExposure.ToString("F2") + ", " + rightwardExposure.ToString("F2");
                    DebugLogWithID("DumpInteractionData", report);
                }
                else
                {
                    DebugLogWithID("DumpInteractionData", "Interaction reference is null, report failed");
                }
            }
            else
            {
                DebugLogWithID("DumpInteractionData", "FAR module not found, report failed");
            }
        }

        #endregion Aero

        #region Alternative UI/input

        public KeyCode uiKeyCodeEdit = KeyCode.J;
        public static float uiMouseDeltaCache = 0f;

        public static int uiInstanceIDTarget = 0;
        private int uiInstanceIDLocal = 0;

        public static int uiPropertySelectionWing = 0;
        public static int uiPropertySelectionSurface = 0;

        public static bool uiEditMode = false;
        public static bool uiEditModeTimeout = false;
        private readonly float uiEditModeTimeoutDuration = 0.25f;
        private float uiEditModeTimer = 0f;

        public Vector2 GetLimits(double value, double step, int i = 0)
        {
            if (value % step != 0 || ((int)(value / step) != i & (int)((value / step) - 1) != i))
                i = (int)(value / step);
            float x = (float)(i * step);
            float y = (float)((i + 1) * step);
            Vector2 limits = new Vector2(x, y);
            return limits;
        }

        public Vector2 GetOffsetLimits(double value, double step, int i = 0)
        {
            value -= step / 2;
            Vector2 limits = GetLimits(value, step, i - 1);
            limits.x -= (float)step / 2;
            limits.y -= (float)step / 2;
            return limits;
            /*
            if (value % step != 0 || ((int)(value / step) != i & (int)((value / step)) != i - 1))
                i = (int)(value / step);
            float x = (float)(i * step - step / 2);
            float y = (float)((i + 1) * step - step / 2);
            Vector2 limits = new Vector2(x, y);
            return limits;
            */
        }
        /*
        public Vector2 switchVector(Vector2 value)
        {
            Vector2 ret;
            ret.x = value.y;
            ret.y = value.x;
            return ret;
        }
        */
        public float GetStep(Vector4 limits)
        {
            float step;
            if (!isCtrlSrf)
                step = limits.y;
            else
                step = limits.w;
            return step;
        }
        public float GetStep2(Vector2 limits)
        {
            return limits.y;
        }

        // Supposed to fix context menu updates
        // Proposed by NathanKell, if I'm not mistaken
        private UIPartActionWindow _myWindow = null;

        private UIPartActionWindow MyWindow
        {
            get
            {
                if (_myWindow == null)
                {
                    // 7/7/2020 CarnationRED: A faster way to get PAW, improves performance
                    _myWindow = part.PartActionWindow;

                    //UIPartActionWindow[] windows = FindObjectsOfType<UIPartActionWindow>();
                    //foreach (UIPartActionWindow window in windows)
                    //{
                    //	if (window.part == part)
                    //	{
                    //		_myWindow = window;
                    //	}
                    //}
                }
                return _myWindow;
            }
        }

        private void UpdateWindow()
        {
            if (MyWindow != null)
            {
                MyWindow.displayDirty = true;
            }
        }

        private void OnGUI()
        {
            if (!isStarted || !HighLogic.LoadedSceneIsEditor || !HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiWindowActive)
            {
                return;
            }

            if (uiInstanceIDLocal == 0)
            {
                uiInstanceIDLocal = part.GetInstanceID();
            }

            if (uiInstanceIDTarget == uiInstanceIDLocal || uiInstanceIDTarget == 0)
            {
                if (!UIUtility.uiStyleConfigured)
                {
                    UIUtility.ConfigureStyles();
                }

                UIUtility.uiRectWindowEditor = GUILayout.Window(GetInstanceID(), UIUtility.uiRectWindowEditor, OnWindow, GetWindowTitle(), UIUtility.uiStyleWindow, GUILayout.Height(HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow ? 0 : UIUtility.uiRectWindowEditor.height));
                HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = false;

                // Thanks to ferram4
                // Following section lock the editor, preventing window clickthrough
                if (UIUtility.uiRectWindowEditor.Contains(UIUtility.GetMousePos()))
                {
                    EditorLogic.fetch.Lock(false, false, false, "WingProceduralWindow");
                    //if (EditorTooltip.Instance != null)
                    //    EditorTooltip.Instance.HideToolTip ();
                }
                else
                {
                    EditorLogic.fetch.Unlock("WingProceduralWindow");
                }
            }
        }

        public static Vector4 uiColorSliderBase = new Vector4(0.25f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderEdgeL = new Vector4(0.20f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderEdgeT = new Vector4(0.15f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderColorsST = new Vector4(0.10f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderColorsSB = new Vector4(0.05f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderColorsET = new Vector4(0.00f, 0.5f, 0.4f, 1f);
        public static Vector4 uiColorSliderColorsEL = new Vector4(0.95f, 0.5f, 0.4f, 1f);

        private void OnWindow(int window)
        {
            if (uiEditMode)
            {
                bool returnEarly = false;
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();

                if (uiLastFieldName.Length > 0)
                {
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000000") + uiLastFieldName, UIUtility.uiStyleLabelMedium);		// #autoLOC_B9_Aerospace_WingStuff_1000000 = Last: 
                }
                else
                {
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000001"), UIUtility.uiStyleLabelMedium);		// #autoLOC_B9_Aerospace_WingStuff_1000001 = Property editor
                }

                if (handlesEnabled && handlesVisible && EditorHandle.AnyHandleDragging)
                {
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000002"), UIUtility.uiStyleLabelHint, GUILayout.MaxHeight(44f), GUILayout.MinHeight(44f)); // 58f for four lines		// #autoLOC_B9_Aerospace_WingStuff_1000002 = LeftCtrl: Auto Axis Locking\nX: lock Offset. Y: lock Length\n_________________________
                }
                else if (uiLastFieldTooltip.Length > 0)
                {
                    GUILayout.Label(uiLastFieldTooltip + Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000003"), UIUtility.uiStyleLabelHint, GUILayout.MaxHeight(44f), GUILayout.MinHeight(44f)); // 58f for four lines		// #autoLOC_B9_Aerospace_WingStuff_1000003 = \n_________________________
                }

                GUILayout.EndVertical();
                GUILayout.BeginVertical();

                if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000004"), UIUtility.uiStyleButton, GUILayout.MaxWidth(50f)))		// #autoLOC_B9_Aerospace_WingStuff_1000004 = Close
                {
                    EditorLogic.fetch.Unlock("WingProceduralWindow");
                    HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiWindowActive = false;
                    returnEarly = true;
                }

                if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000005"), UIUtility.uiStyleButton, GUILayout.MaxWidth(50f)))		// #autoLOC_B9_Aerospace_WingStuff_1000005 = Handles
                {
                    handlesVisible = !handlesVisible;
                    StaticWingGlobals.handlesRoot.SetActive(handlesVisible);
                }

                if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000131"), UIUtility.uiStyleButton, GUILayout.MaxWidth(50f)))		// #autoLOC_B9_Aerospace_WingStuff_1000131 = #
                {
                    UIUtility.numericInput = !UIUtility.numericInput;
                }

                GUILayout.EndVertical();

                GUILayout.EndHorizontal();

                if (returnEarly)
                {
                    return;
                }
                DrawFieldGroupHeader(ref sharedFieldPrefStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000006"));		// #autoLOC_B9_Aerospace_WingStuff_1000006 = Preference
                if (sharedFieldPrefStatic)
                {
                    DrawCheck(ref sharedPropAnglePref, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000007"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000008"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000009"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000156"), 101);		// #autoLOC_B9_Aerospace_WingStuff_1000007 = Use angles to define the wing		// #autoLOC_B9_Aerospace_WingStuff_1000008 = No		// #autoLOC_B9_Aerospace_WingStuff_1000009 = Yes		// #autoLOC_B9_Aerospace_WingStuff_1000156 = AngleDefine
                    DrawCheck(ref sharedPropEThickPref, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000010"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000011"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000012"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000157"), 103);		// #autoLOC_B9_Aerospace_WingStuff_1000010 = Scale edges to thickness 		// #autoLOC_B9_Aerospace_WingStuff_1000011 = No		// #autoLOC_B9_Aerospace_WingStuff_1000012 = Yes		// #autoLOC_B9_Aerospace_WingStuff_1000157 = ThickScale
                    //DrawCheck(ref sharedArmorPref, "Make wings more durable!!!", "UnArmored", "Armored", "Armored Wings",104);
                    DrawCheck(ref sharedPropEdgePref, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000158"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000159"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000160"), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000161"), 102);		// #autoLOC_B9_Aerospace_WingStuff_1000158 = Include edges in definitions		// #autoLOC_B9_Aerospace_WingStuff_1000159 = No		// #autoLOC_B9_Aerospace_WingStuff_1000160 = Yes		// #autoLOC_B9_Aerospace_WingStuff_1000161 = EdgeIncluded
                    if (sharedPropAnglePref)
                    {
                        DrawCheck(ref sharedPropLockPref, "Lock Tip width instead of base width", "No", "Yes", "Lock Tip", 105);
                        DrawCheck(ref sharedPropLock2Pref, "Lock Tip mid-point instead of base", "No", "Yes", "Lock Tip", 106);
                        DrawCheck(ref sharedPropLock3Pref, "Lock width and change offset only", "No", "Yes", "Lock Tip", 107);
                    }
                }
                DrawFieldGroupHeader(ref sharedFieldGroupBaseStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000013"));		// #autoLOC_B9_Aerospace_WingStuff_1000013 = Base
                if (sharedFieldGroupBaseStatic & !isCtrlSrf)
                {
                    if (sharedArmorPref)
                    {
                        DrawLimited(ref sharedArmorRatio, 10, 100, sharedArmorLimits, "ReinforceRatio", uiColorSliderBase, 301, 0, true);
                    }
                    DrawField(ref sharedBaseLength, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseLengthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000014"), uiColorSliderBase, 0, 0, ref sharedBaseLengthInt);		// #autoLOC_B9_Aerospace_WingStuff_1000014 = Length
                    if (!sharedPropAnglePref)
                    {
                        DrawLimited(ref sharedBaseWidthRoot, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthRootLimits), GetLimitsFromType(sharedBaseWidthRootLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000015"), uiColorSliderBase, 1, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000015 = Width (root)
                        
                        // merge conflict here
                        //DrawField(ref sharedBaseWidthRoot, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthRootLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000015"), uiColorSliderBase, 1, 0, ref sharedBaseWidthRInt);		// #autoLOC_B9_Aerospace_WingStuff_1000015 = Width (root)
                        DrawField(ref sharedBaseWidthTip, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthTipLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000016"), uiColorSliderBase, 2, 0, ref sharedBaseWidthTInt, true);		// #autoLOC_B9_Aerospace_WingStuff_1000016 = Width (tip)
                        DrawOffset(ref sharedBaseOffsetTip, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseOffsetLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000017"), uiColorSliderBase, 4, 0, ref sharedBaseOffsetTInt, true);		// #autoLOC_B9_Aerospace_WingStuff_1000017 = Offset (tip)
                    }
                    else
                    {
                        //dummyValueInt = 0;
                        DrawLimited(ref sharedSweptAngleFront, sharedIncrementAngle, sharedIncrementAngleLarge, sharedSweptAngleLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000018"), uiColorSliderBase, 201, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000018 = Swept angle(front)
                        //dummyValueInt = 0;
                        sharedSweptAngleBack = CalcAngleBack();
                        DrawLimited(ref sharedSweptAngleBack, sharedIncrementAngle, sharedIncrementAngleLarge, sharedSweptAngleLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000019"), uiColorSliderBase, 202, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000019 = Swept angle(back)
                        sharedSweptAngleFront = CalcAngleFront();
                        if (sharedPropLockPref)
                        {
                            DrawField(ref sharedBaseWidthTip, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthTipLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000016"), uiColorSliderBase, 2, 0, ref sharedBaseWidthTInt, true);		// #autoLOC_B9_Aerospace_WingStuff_1000016 = Width (tip)
                        }
                        else if (!sharedPropLockPref)
                        {

                            DrawLimited(ref sharedBaseWidthRoot, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthRootLimits), GetLimitsFromType(sharedBaseWidthRootLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000015"), uiColorSliderBase, 1, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000015 = Width (root)
                        }
                    }

                    DrawLimited(ref sharedBaseThicknessRoot, sharedIncrementSmall, GetStep2(sharedBaseThicknessLimits), sharedBaseThicknessLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000020"), uiColorSliderBase, 5, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000020 = Thickness (root)
                    DrawLimited(ref sharedBaseThicknessTip, sharedIncrementSmall, GetStep2(sharedBaseThicknessLimits), sharedBaseThicknessLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000021"), uiColorSliderBase, 6, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000021 = Thickness (tip)

                    //Debug.Log("B9PW: base complete");
                }
                else if (sharedFieldGroupBaseStatic & isCtrlSrf)
                {
                    DrawField(ref sharedBaseLength, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseLengthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000022"), uiColorSliderBase, 0, 0, ref sharedBaseLengthInt);		// #autoLOC_B9_Aerospace_WingStuff_1000022 = Length

                    DrawLimited(ref sharedBaseWidthRoot, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthRootLimits), GetLimitsFromType(sharedBaseWidthRootLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000023"), uiColorSliderBase, 1, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000023 = Width (root)
                    DrawField(ref sharedBaseWidthTip, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseWidthTipLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000024"), uiColorSliderBase, 2, 0, ref sharedBaseWidthTInt);		// #autoLOC_B9_Aerospace_WingStuff_1000024 = Width (tip)
                    DrawOffset(ref sharedBaseOffsetRoot, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseOffsetLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000025"), uiColorSliderBase, 3, 0, ref sharedBaseOffsetRInt);		// #autoLOC_B9_Aerospace_WingStuff_1000025 = Offset (root)
                    DrawOffset(ref sharedBaseOffsetTip, GetIncrementFromType(sharedIncrementMain, sharedIncrementSmall), GetStep(sharedBaseOffsetLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000026"), uiColorSliderBase, 4, 0, ref sharedBaseOffsetTInt);		// #autoLOC_B9_Aerospace_WingStuff_1000026 = Offset (tip)
                    DrawLimited(ref sharedBaseThicknessRoot, sharedIncrementSmall, GetStep2(sharedBaseThicknessLimits), sharedBaseThicknessLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000027"), uiColorSliderBase, 5, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000027 = Thickness (root)
                    DrawLimited(ref sharedBaseThicknessTip, sharedIncrementSmall, GetStep2(sharedBaseThicknessLimits), sharedBaseThicknessLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000028"), uiColorSliderBase, 6, 0);		// #autoLOC_B9_Aerospace_WingStuff_1000028 = Thickness (tip)

                }

                if (!isCtrlSrf)
                {
                    DrawFieldGroupHeader(ref sharedFieldGroupEdgeLeadingStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000029"));		// #autoLOC_B9_Aerospace_WingStuff_1000029 = Edge (leading)
                    if (sharedFieldGroupEdgeLeadingStatic)
                    {

                        Vector2 edgeLimits = GetLimitsFromType(sharedEdgeTypeLimits);
                        DrawInt(ref sharedEdgeTypeLeading, sharedIncrementInt, (int)edgeLimits.x, (int)edgeLimits.y, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000030"), uiColorSliderEdgeL, 7, 2);		// #autoLOC_B9_Aerospace_WingStuff_1000030 = Shape

                        DrawField(ref sharedEdgeWidthLeadingRoot, sharedIncrementSmall, GetStep(sharedEdgeWidthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000031"), uiColorSliderEdgeL, 8, 0, ref sharedEdgeWidthLRInt);		// #autoLOC_B9_Aerospace_WingStuff_1000031 = Width (root)
                        DrawField(ref sharedEdgeWidthLeadingTip, sharedIncrementSmall, GetStep(sharedEdgeWidthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000032"), uiColorSliderEdgeL, 9, 0, ref sharedEdgeWidthLTInt);		// #autoLOC_B9_Aerospace_WingStuff_1000032 = Width (tip)
                    }

                }

                DrawFieldGroupHeader(ref sharedFieldGroupEdgeTrailingStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000033"));		// #autoLOC_B9_Aerospace_WingStuff_1000033 = Edge (trailing)
                if (sharedFieldGroupEdgeTrailingStatic)
                {

                    Vector2 edgeLimits = GetLimitsFromType(sharedEdgeTypeLimits);
                    DrawInt(ref sharedEdgeTypeTrailing, sharedIncrementInt, (int)edgeLimits.x, (int)edgeLimits.y, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000034"), uiColorSliderEdgeT, 10, isCtrlSrf ? 3 : 2);		// #autoLOC_B9_Aerospace_WingStuff_1000034 = Shape

                    DrawField(ref sharedEdgeWidthTrailingRoot, sharedIncrementSmall, GetStep(sharedEdgeWidthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000035"), uiColorSliderEdgeT, 11, 0, ref sharedEdgeWidthTRInt);		// #autoLOC_B9_Aerospace_WingStuff_1000035 = Width (root)
                    DrawField(ref sharedEdgeWidthTrailingTip, sharedIncrementSmall, GetStep(sharedEdgeWidthLimits), Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000036"), uiColorSliderEdgeT, 12, 0, ref sharedEdgeWidthTTInt);		// #autoLOC_B9_Aerospace_WingStuff_1000036 = Width (tip)
                }

                if (ApplyLegacyTextures())
                {
                    DrawFieldGroupHeader(ref sharedFieldGroupColorSTStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000037"));		// #autoLOC_B9_Aerospace_WingStuff_1000037 = Surface (top)
                    if (sharedFieldGroupColorSTStatic)
                    {
                        DrawInt(ref sharedMaterialST, sharedIncrementInt, 0, 4, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000038"), uiColorSliderColorsST, 13, 1);		// #autoLOC_B9_Aerospace_WingStuff_1000038 = Material
                        DrawLimited(ref sharedColorSTOpacity, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000039"), uiColorSliderColorsST, 14, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000039 = Opacity
                        DrawLimited(ref sharedColorSTHue, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000040"), uiColorSliderColorsST, 15, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000040 = Hue
                        DrawLimited(ref sharedColorSTSaturation, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000041"), uiColorSliderColorsST, 16, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000041 = Saturation
                        DrawLimited(ref sharedColorSTBrightness, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000042"), uiColorSliderColorsST, 17, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000042 = Brightness
                    }

                    DrawFieldGroupHeader(ref sharedFieldGroupColorSBStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000043"));		// #autoLOC_B9_Aerospace_WingStuff_1000043 = Surface (bottom)
                    if (sharedFieldGroupColorSBStatic)
                    {
                        DrawInt(ref sharedMaterialSB, sharedIncrementInt, 0, 4, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000044"), uiColorSliderColorsSB, 13, 1);		// #autoLOC_B9_Aerospace_WingStuff_1000044 = Material
                        DrawLimited(ref sharedColorSBOpacity, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000045"), uiColorSliderColorsSB, 14, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000045 = Opacity
                        DrawLimited(ref sharedColorSBHue, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000046"), uiColorSliderColorsSB, 15, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000046 = Hue
                        DrawLimited(ref sharedColorSBSaturation, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000047"), uiColorSliderColorsSB, 16, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000047 = Saturation
                        DrawLimited(ref sharedColorSBBrightness, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000048"), uiColorSliderColorsSB, 17, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000048 = Brightness
                    }

                    DrawFieldGroupHeader(ref sharedFieldGroupColorETStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000049"));		// #autoLOC_B9_Aerospace_WingStuff_1000049 = Surface (trailing edge)
                    if (sharedFieldGroupColorETStatic)
                    {
                        DrawInt(ref sharedMaterialET, sharedIncrementInt, 0, 4, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000050"), uiColorSliderColorsET, 13, 1);		// #autoLOC_B9_Aerospace_WingStuff_1000050 = Material
                        DrawLimited(ref sharedColorETOpacity, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000051"), uiColorSliderColorsET, 14, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000051 = Opacity
                        DrawLimited(ref sharedColorETHue, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000052"), uiColorSliderColorsET, 15, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000052 = Hue
                        DrawLimited(ref sharedColorETSaturation, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000053"), uiColorSliderColorsET, 16, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000053 = Saturation
                        DrawLimited(ref sharedColorETBrightness, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000054"), uiColorSliderColorsET, 17, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000054 = Brightness
                    }

                    if (!isCtrlSrf)
                    {
                        DrawFieldGroupHeader(ref sharedFieldGroupColorELStatic, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000055"));		// #autoLOC_B9_Aerospace_WingStuff_1000055 = Surface (leading edge)
                        if (sharedFieldGroupColorELStatic)
                        {
                            DrawInt(ref sharedMaterialEL, sharedIncrementInt, 0, 4, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000056"), uiColorSliderColorsEL, 13, 1);		// #autoLOC_B9_Aerospace_WingStuff_1000056 = Material
                            DrawLimited(ref sharedColorELOpacity, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000057"), uiColorSliderColorsEL, 14, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000057 = Opacity
                            DrawLimited(ref sharedColorELHue, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000058"), uiColorSliderColorsEL, 15, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000058 = Hue
                            DrawLimited(ref sharedColorELSaturation, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000059"), uiColorSliderColorsEL, 16, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000059 = Saturation
                            DrawLimited(ref sharedColorELBrightness, sharedIncrementColor, sharedIncrementColorLarge, sharedColorLimits, Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000060"), uiColorSliderColorsEL, 17, 0, true);		// #autoLOC_B9_Aerospace_WingStuff_1000060 = Brightness
                        }
                    }
                }

                GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000061"), UIUtility.uiStyleLabelHint);		// #autoLOC_B9_Aerospace_WingStuff_1000061 = _________________________\n\nPress J to exit edit mode\nOptions below allow you to change default values
                if (CanBeFueled && UseStockFuel && GUILayout.Button(FuelGUIGetConfigDesc() + Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000062"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000062 =  | Next tank setup
                {
                    NextConfiguration();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000063"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000063 = Save as default
                {
                    ReplaceDefaults();
                }

                if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000064"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000064 = Restore default
                {
                    RestoreDefaults();
                }

                GUILayout.EndHorizontal();
                if (inheritancePossibleOnShape || inheritancePossibleOnMaterials)
                {
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000065"), UIUtility.uiStyleLabelHint);		// #autoLOC_B9_Aerospace_WingStuff_1000065 = _________________________\n\nOptions options allow you to match the part properties to it's parent
                    GUILayout.BeginHorizontal();

                    if (inheritancePossibleOnShape)
                    {
                        if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000066"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000066 = Shape
                        {
                            InheritParentValues(0);
                        }

                        if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000067"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000067 = Base
                        {
                            InheritParentValues(1);
                        }

                        if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000068"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000068 = Edges
                        {
                            InheritParentValues(2);
                        }
                    }

                    if (inheritancePossibleOnMaterials && GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000069"), UIUtility.uiStyleButton))		// #autoLOC_B9_Aerospace_WingStuff_1000069 = Color
                    {
                        InheritParentValues(3);
                    }

                    GUILayout.EndHorizontal();
                    if (isCtrlSrf)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000070"), UIUtility.uiStyleButton)) InheritParentValues(4, true);		// #autoLOC_B9_Aerospace_WingStuff_1000070 = Align with back edges
                        if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000071"), UIUtility.uiStyleButton)) InheritParentValues(4, false);		// #autoLOC_B9_Aerospace_WingStuff_1000071 = Align with fore edges
                        GUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                if (uiEditModeTimeout)
                {
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000072"), UIUtility.uiStyleLabelMedium);		// #autoLOC_B9_Aerospace_WingStuff_1000072 = Exiting edit mode...\n
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000073"), UIUtility.uiStyleLabelHint);		// #autoLOC_B9_Aerospace_WingStuff_1000073 = Press J while pointing at a\nprocedural part to edit it
                    if (GUILayout.Button(Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000155"), UIUtility.uiStyleButton, GUILayout.MaxWidth(50f)))		// #autoLOC_B9_Aerospace_WingStuff_1000155 = Close
                    {
                        HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiWindowActive = false;
                        HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = true;
                        EditorLogic.fetch.Unlock("WingProceduralWindow");
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUI.DragWindow();
        }

        private void SetupFields()
        {

            sharedArmorRatio = SetupFieldValue(sharedArmorRatio, sharedArmorLimits, 0);
            sharedBaseLength = SetupFieldValue(sharedBaseLength, positiveinf, GetDefault(sharedBaseLengthDefaults));
            sharedBaseWidthRoot = SetupFieldValue(sharedBaseWidthRoot, positiveinf, GetDefault(sharedBaseWidthRootDefaults));
            sharedBaseWidthTip = SetupFieldValue(sharedBaseWidthTip, positiveinf, GetDefault(sharedBaseWidthTipDefaults));
            sharedBaseThicknessRoot = SetupFieldValue(sharedBaseThicknessRoot, positiveinf, GetDefault(sharedBaseThicknessRootDefaults));
            sharedBaseThicknessTip = SetupFieldValue(sharedBaseThicknessTip, positiveinf, GetDefault(sharedBaseThicknessTipDefaults));
            sharedBaseOffsetRoot = SetupFieldValue(sharedBaseOffsetRoot, nolimit, GetDefault(sharedBaseOffsetRootDefaults));
            sharedBaseOffsetTip = SetupFieldValue(sharedBaseOffsetTip, nolimit, GetDefault(sharedBaseOffsetTipDefaults));

            sharedEdgeTypeTrailing = SetupFieldValue(sharedEdgeTypeTrailing, GetLimitsFromType(sharedEdgeTypeLimits), GetDefault(sharedEdgeTypeTrailingDefaults));
            sharedEdgeWidthTrailingRoot = SetupFieldValue(sharedEdgeWidthTrailingRoot, positiveinf, GetDefault(sharedEdgeWidthTrailingRootDefaults));
            sharedEdgeWidthTrailingTip = SetupFieldValue(sharedEdgeWidthTrailingTip, positiveinf, GetDefault(sharedEdgeWidthTrailingTipDefaults));

            sharedEdgeTypeLeading = SetupFieldValue(sharedEdgeTypeLeading, GetLimitsFromType(sharedEdgeTypeLimits), GetDefault(sharedEdgeTypeLeadingDefaults));

            sharedEdgeWidthLeadingRoot = SetupFieldValue(sharedEdgeWidthLeadingRoot, positiveinf, GetDefault(sharedEdgeWidthLeadingRootDefaults));
            sharedEdgeWidthLeadingTip = SetupFieldValue(sharedEdgeWidthLeadingTip, positiveinf, GetDefault(sharedEdgeWidthLeadingTipDefaults));

            sharedMaterialST = SetupFieldValue(sharedMaterialST, sharedMaterialLimits, GetDefault(sharedMaterialSTDefaults));
            sharedColorSTOpacity = SetupFieldValue(sharedColorSTOpacity, sharedColorLimits, GetDefault(sharedColorSTOpacityDefaults));
            sharedColorSTHue = SetupFieldValue(sharedColorSTHue, sharedColorLimits, GetDefault(sharedColorSTHueDefaults));
            sharedColorSTSaturation = SetupFieldValue(sharedColorSTSaturation, sharedColorLimits, GetDefault(sharedColorSTSaturationDefaults));
            sharedColorSTBrightness = SetupFieldValue(sharedColorSTBrightness, sharedColorLimits, GetDefault(sharedColorSTBrightnessDefaults));

            sharedMaterialSB = SetupFieldValue(sharedMaterialSB, sharedMaterialLimits, GetDefault(sharedMaterialSBDefaults));
            sharedColorSBOpacity = SetupFieldValue(sharedColorSBOpacity, sharedColorLimits, GetDefault(sharedColorSBOpacityDefaults));
            sharedColorSBHue = SetupFieldValue(sharedColorSBHue, sharedColorLimits, GetDefault(sharedColorSBHueDefaults));
            sharedColorSBSaturation = SetupFieldValue(sharedColorSBSaturation, sharedColorLimits, GetDefault(sharedColorSBSaturationDefaults));
            sharedColorSBBrightness = SetupFieldValue(sharedColorSBBrightness, sharedColorLimits, GetDefault(sharedColorSBBrightnessDefaults));

            sharedMaterialET = SetupFieldValue(sharedMaterialET, sharedMaterialLimits, GetDefault(sharedMaterialETDefaults));
            sharedColorETOpacity = SetupFieldValue(sharedColorETOpacity, sharedColorLimits, GetDefault(sharedColorETOpacityDefaults));
            sharedColorETHue = SetupFieldValue(sharedColorETHue, sharedColorLimits, GetDefault(sharedColorETHueDefaults));
            sharedColorETSaturation = SetupFieldValue(sharedColorETSaturation, sharedColorLimits, GetDefault(sharedColorETSaturationDefaults));
            sharedColorETBrightness = SetupFieldValue(sharedColorETBrightness, sharedColorLimits, GetDefault(sharedColorETBrightnessDefaults));

            sharedMaterialEL = SetupFieldValue(sharedMaterialEL, sharedMaterialLimits, GetDefault(sharedMaterialELDefaults));
            sharedColorELOpacity = SetupFieldValue(sharedColorELOpacity, sharedColorLimits, GetDefault(sharedColorELOpacityDefaults));
            sharedColorELHue = SetupFieldValue(sharedColorELHue, sharedColorLimits, GetDefault(sharedColorELHueDefaults));
            sharedColorELSaturation = SetupFieldValue(sharedColorELSaturation, sharedColorLimits, GetDefault(sharedColorELSaturationDefaults));
            sharedColorELBrightness = SetupFieldValue(sharedColorELBrightness, sharedColorLimits, GetDefault(sharedColorELBrightnessDefaults));

            UpdateWindow();
            isSetToDefaultValues = true;
        }

        private int GetFieldMode()
        {
            return isCtrlSrf ? 2 : 1;
        }

        private float SetupFieldValue(float value, Vector2 limits, float defaultValue)
        {
            return isSetToDefaultValues ? Mathf.Clamp(value, limits.x, limits.y) : defaultValue;
        }
        /*{
            if (!isSetToDefaultValues)
                return defaultValue;
            else
                return value;
        }*/
        // bypass limit check
        /// <summary>
        ///
        /// </summary>
        /// <param name="field">the value to draw</param>
        /// <param name="increment">mouse drag increment</param>
        /// <param name="incrementLarge">button increment</param>

        /// <param name="name">the field name to display</param>
        /// <param name="hsbColor">field colour</param>
        /// <param name="fieldID">tooltip stuff</param>
        /// <param name="fieldType">tooltip stuff</param>
        /// <param name="allowFine">Whether right click drag behaves as fine control or not</param>
        private void DrawField(ref float field, float increment, float step, string name, Vector4 hsbColor, int fieldID, int fieldType, ref int delta, bool allowFine = true)
        {

            float cached = field;

            field = UIUtility.FieldSlider(field, increment, step, name, out bool changed, ColorHSBToRGB(hsbColor), fieldType, ref delta, allowFine);

            if (changed)
            {

                HandleFieldValueChange(field, name, fieldID, cached);

            }
        }

        private void DrawOffset(ref float field, float increment, float range, string name, Vector4 hsbColor, int fieldID, int fieldType, ref int delta, bool allowFine = true)
        {

            float cached = field;

            field = UIUtility.OffsetSlider(field, increment, range, name, out bool changed, ColorHSBToRGB(hsbColor), fieldType, ref delta, allowFine);

            if (changed)
            {

                HandleFieldValueChange(field, name, fieldID, cached);
            }
        }

        private void DrawLimited(ref float field, float increment, float incrementLarge, Vector2 limits, string name, Vector4 hsbColor, int fieldID, int fieldType, bool allowFine = true)
        {
            float cached = field;

            field = UIUtility.LimitedSlider(field, increment, incrementLarge, limits, name, out bool changed, ColorHSBToRGB(hsbColor), fieldType, allowFine);

            if (changed)
            {

                HandleFieldValueChange(field, name, fieldID, cached);
            }
        }

        private void DrawInt(ref float field, float incrementLarge, int min, int max, string name, Vector4 hsbColor, int fieldID, int fieldType, bool allowFine = true)
        {
            float cached = field;

            field = UIUtility.IntegerSlider(field, incrementLarge, min, max, name, out bool changed, ColorHSBToRGB(hsbColor), fieldType, allowFine);

            if (changed)
            {

                HandleFieldValueChange(field, name, fieldID, cached);

            }
        }

        private void DrawCheck(ref bool value, string desc, string choice1, string choice2, string name, int fieldID)
        {
            value = UIUtility.CheckBox(desc, choice1, choice2, value, out bool changed);
            if (changed)
            {
                uiLastFieldName = name;
                uiLastFieldTooltip = UpdateTooltipText(fieldID);
                if (fieldID == 101 && sharedPropAnglePref == true)
                {
                    sharedSweptAngleBack = CalcAngleBack();
                    sharedSweptAngleFront = CalcAngleFront();
                }
                //Debug.Log("B9PW:" + value + " Value changed to " + value);
            }
        }


        private void HandleFieldValueChange(float field, string name, int fieldID, float cached)
        {
            uiLastFieldName = name;
            uiLastFieldTooltip = UpdateTooltipText(fieldID);
            if (fieldID == 5 & sharedPropEThickPref)
            {
                if (cached == 0)
                    cached = field;
                sharedEdgeWidthLeadingRoot *= field / cached;
                sharedEdgeWidthTrailingRoot *= field / cached;
            }
            else if (fieldID == 6 & sharedPropEThickPref)
            {
                if (cached == 0)
                    cached = field;
                sharedEdgeWidthLeadingTip *= field / cached;
                sharedEdgeWidthTrailingTip *= field / cached;
            }
            else if (fieldID == 201 || fieldID == 202)
            {
                CalcBase(fieldID);
            }
        }


        private void DrawFieldGroupHeader(ref bool fieldGroupBoolStatic, string header)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(header, UIUtility.uiStyleLabelHint))
            {
                fieldGroupBoolStatic = !fieldGroupBoolStatic;
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logPropertyWindow)
                {
                    DebugLogWithID("DrawFieldGroupHeader", "Header of " + header + " pressed | Group state: " + fieldGroupBoolStatic);
                }

                HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = true;
            }
            if (fieldGroupBoolStatic)
            {
                GUILayout.Label("|", UIUtility.uiStyleLabelHint, GUILayout.MaxWidth(15f));
            }
            else
            {
                GUILayout.Label("+", UIUtility.uiStyleLabelHint, GUILayout.MaxWidth(15f));
            }

            GUILayout.EndHorizontal();
        }

        private static string uiLastFieldName = "";
        private static string uiLastFieldTooltip = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000074");		// #autoLOC_B9_Aerospace_WingStuff_1000074 = Additional info on edited \nproperties is displayed here

        private string UpdateTooltipText(int fieldID)
        {
            // Base descriptions
            if (fieldID == 0) // sharedBaseLength))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000075")		// #autoLOC_B9_Aerospace_WingStuff_1000075 = Lateral measurement of the wing, \nalso referred to as semispan
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000076");		// #autoLOC_B9_Aerospace_WingStuff_1000076 = Lateral measurement of the control \nsurface at it's root
            }
            else if (fieldID == 1) // sharedBaseWidthRoot))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000077")		// #autoLOC_B9_Aerospace_WingStuff_1000077 = Longitudinal measurement of the wing \nat the root cross section
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000078");		// #autoLOC_B9_Aerospace_WingStuff_1000078 = Longitudinal measurement of \nthe root chord
            }
            else if (fieldID == 2) // sharedBaseWidthTip))
            {
                return !isCtrlSrf ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000079") : "Longitudinal measurement of \nthe tip chord";		// #autoLOC_B9_Aerospace_WingStuff_1000079 = Longitudinal measurement of the wing \nat the tip cross section
            }
            else if (fieldID == 3) // sharedBaseOffsetRoot))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000080")		// #autoLOC_B9_Aerospace_WingStuff_1000080 = This property shouldn't be accessible \non a wing
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000081");		// #autoLOC_B9_Aerospace_WingStuff_1000081 = Offset of the trailing edge \nroot corner on the lateral axis
            }
            else if (fieldID == 4) // sharedBaseOffsetTip))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000082")		// #autoLOC_B9_Aerospace_WingStuff_1000082 = Distance between midpoints of the cross \nsections on the longitudinal axis
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000083");		// #autoLOC_B9_Aerospace_WingStuff_1000083 = Offset of the trailing edge \ntip corner on the lateral axis
            }
            else if (fieldID == 5) // sharedBaseThicknessRoot))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000084")		// #autoLOC_B9_Aerospace_WingStuff_1000084 = Thickness at the root cross section \nUsually kept proportional to edge width
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000085");		// #autoLOC_B9_Aerospace_WingStuff_1000085 = Thickness at the root cross section \nUsually kept proportional to edge width
            }
            else if (fieldID == 6) // sharedBaseThicknessTip))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000086")		// #autoLOC_B9_Aerospace_WingStuff_1000086 = Thickness at the tip cross section \nUsually kept proportional to edge width
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000087");		// #autoLOC_B9_Aerospace_WingStuff_1000087 = Thickness at the tip cross section \nUsually kept proportional to edge width
            }

            // Edge descriptions
            else if (fieldID == 7) // sharedEdgeTypeTrailing))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000088")		// #autoLOC_B9_Aerospace_WingStuff_1000088 = Shape of the trailing edge cross \nsection (round/biconvex/sharp)
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000089");		// #autoLOC_B9_Aerospace_WingStuff_1000089 = Shape of the trailing edge cross \nsection (round/biconvex/sharp)
            }
            else if (fieldID == 8) // sharedEdgeWidthTrailingRoot))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000090")		// #autoLOC_B9_Aerospace_WingStuff_1000090 = Longitudinal measurement of the trailing \nedge cross section at wing root
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000091");		// #autoLOC_B9_Aerospace_WingStuff_1000091 = Longitudinal measurement of the trailing \nedge cross section at with root
            }
            else if (fieldID == 9) // sharedEdgeWidthTrailingTip))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000092")		// #autoLOC_B9_Aerospace_WingStuff_1000092 = Longitudinal measurement of the trailing \nedge cross section at wing tip
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000093");		// #autoLOC_B9_Aerospace_WingStuff_1000093 = Longitudinal measurement of the trailing \nedge cross section at with tip
            }
            else if (fieldID == 10) // sharedEdgeTypeLeading))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000094")		// #autoLOC_B9_Aerospace_WingStuff_1000094 = Shape of the leading edge cross \nsection (round/biconvex/sharp)
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000095");		// #autoLOC_B9_Aerospace_WingStuff_1000095 = Shape of the leading edge cross \nsection (round/biconvex/sharp)
            }
            else if (fieldID == 11) // sharedEdgeWidthLeadingRoot))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000096")		// #autoLOC_B9_Aerospace_WingStuff_1000096 = Longitudinal measurement of the leading \nedge cross section at wing root
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000097");		// #autoLOC_B9_Aerospace_WingStuff_1000097 = Longitudinal measurement of the leading \nedge cross section at wing root
            }
            else if (fieldID == 12) // sharedEdgeWidthLeadingTip))
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000098")		// #autoLOC_B9_Aerospace_WingStuff_1000098 = Longitudinal measurement of the leading \nedge cross section at with tip
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000099");		// #autoLOC_B9_Aerospace_WingStuff_1000099 = Longitudinal measurement of the leading \nedge cross section at with tip
            }

            // Surface descriptions
            else if (fieldID == 13)
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000100")		// #autoLOC_B9_Aerospace_WingStuff_1000100 = Surface material (uniform fill, plating, \nLRSI/HRSI tiles and so on)
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000101");		// #autoLOC_B9_Aerospace_WingStuff_1000101 = Surface material (uniform fill, plating, \nLRSI/HRSI tiles and so on)
            }
            else if (fieldID == 14)
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000102")		// #autoLOC_B9_Aerospace_WingStuff_1000102 = Fairly self-explanatory, controls the paint \nopacity: no paint at 0, full coverage at 1
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000103");		// #autoLOC_B9_Aerospace_WingStuff_1000103 = Fairly self-explanatory, controls the paint \nopacity: no paint at 0, full coverage at 1
            }
            else if (fieldID == 15)
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000104")		// #autoLOC_B9_Aerospace_WingStuff_1000104 = Controls the paint hue (HSB axis): \nvalues from zero to one make full circle
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000105");		// #autoLOC_B9_Aerospace_WingStuff_1000105 = Controls the paint hue (HSB axis): \nvalues from zero to one make full circle
            }
            else if (fieldID == 16)
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000106")		// #autoLOC_B9_Aerospace_WingStuff_1000106 = Controls the paint saturation (HSB axis): \ncolorless at 0, full color at 1
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000107");		// #autoLOC_B9_Aerospace_WingStuff_1000107 = Controls the paint saturation (HSB axis): \ncolorless at 0, full color at 1
            }
            else if (fieldID == 17)
            {
                return !isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000108")		// #autoLOC_B9_Aerospace_WingStuff_1000108 = Controls the paint brightness (HSB axis): black at 0, white at 1, primary at 0.5
                    : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000109");		// #autoLOC_B9_Aerospace_WingStuff_1000109 = Controls the paint brightness (HSB axis): black at 0, white at 1, primary at 0.5
            }
            else if (fieldID == 101)
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000110");		// #autoLOC_B9_Aerospace_WingStuff_1000110 = Use front and back sweptback angles to define wings,\nor just select no to use the good old lengths.
            else if (fieldID == 102)
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000162");		// #autoLOC_B9_Aerospace_WingStuff_1000162 = Include or exclude edges \nwhen changing propertiesof the wing.
            else if (fieldID == 103)
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000111");		// #autoLOC_B9_Aerospace_WingStuff_1000111 = Scale edge lengths when changing thickness.
            else if (fieldID == 104)
                return "not yet implemented";
            else if (fieldID == 105)
                return "Change wing root width \ninstead of wing tip for angle define ";
            else if (fieldID == 106)
                return "Lock wing tip offset \n instead of wing root offset for angle define";
            else if (fieldID == 107)
                return "Lock wing width \n while modify angles";
            else if (fieldID == 201)
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000112");		// #autoLOC_B9_Aerospace_WingStuff_1000112 = Angle between front edge and root.\n<90 deg is to the back
            else if (fieldID == 202)
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000113");		// #autoLOC_B9_Aerospace_WingStuff_1000113 = Angle between back edge and root.\n<90 deg is to the back.
            else if (fieldID == 301)
                return "Amount of crash tolerance you would like to add";

            else // This should not really happen
            {
                return Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000114");		// #autoLOC_B9_Aerospace_WingStuff_1000114 = Unknown field\n
            }
        }

        private void OnMouseOver()
        {
            if (!HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

            if (part.parent != null && isAttached && !uiEditModeTimeout)
            {
                if (uiEditMode)
                {
                    if (Input.GetKeyDown(KeyCode.Mouse1))
                    {
                        uiEditMode = false;
                        uiEditModeTimeout = true;
                    }
                }

                if (Input.GetKeyDown(uiKeyCodeEdit))
                {
                    uiInstanceIDTarget = part.GetInstanceID();
                    uiEditMode = true;
                    uiEditModeTimeout = true;
                    HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = true;
                    HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiWindowActive = true;
                    InheritanceStatusUpdate();
                }
            }

            if (state == 0)
            {
                lastMousePos = Input.mousePosition;
                state =
                    Input.GetKeyDown(keyTranslation)
                        ? 1
                    : Input.GetKeyDown(keyTipWidth)
                        ? 2
                    : Input.GetKeyDown(keyRootWidth)
                        ? 3
                    : state
                ;
            }
        }

        private static readonly KeyCode keyTranslation = KeyCode.G, keyTipWidth = KeyCode.T, keyRootWidth = KeyCode.B, keyLeading = KeyCode.LeftAlt, keyTrailing = KeyCode.LeftControl;
        private Vector3 lastMousePos;
        private int state = 0; // 0 == nothing, 1 == translate, 2 == tipScale, 3 == rootScale
        public static Camera editorCam;
        public void DeformWing()
        {
            if (!isAttached || state == 0)
            {
                return;
            }

            float depth = EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).WorldToScreenPoint(part.transform.position).z;
            Vector3 diff = depth * (Input.mousePosition - lastMousePos) / 1000;
            lastMousePos = Input.mousePosition;
            switch (state)
            {
                case 1:
                    if (!Input.GetKey(keyTranslation))
                    {
                        state = 0;
                        return;
                    }

                    sharedBaseLength += (isCtrlSrf ? 2 : 1) * diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.right) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.right);
                    //sharedBaseLength = Mathf.Clamp(sharedBaseLength, GetLimitsFromType(sharedBaseLengthLimits).x, GetLimitsFromType(sharedBaseLengthLimits).y);

                    if (!isCtrlSrf)
                    {
                        sharedBaseOffsetTip -= diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.up);
                        //sharedBaseOffsetTip = Mathf.Clamp(sharedBaseOffsetTip, GetLimitsFromType(sharedBaseOffsetLimits).x, GetLimitsFromType(sharedBaseOffsetLimits).y);

                        sharedBaseLength += (isCtrlSrf ? 2 : 1) * diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.right) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.right);
                        sharedBaseLength = Mathf.Clamp(sharedBaseLength, GetLimitsFromType(sharedBaseLengthLimits).x, GetLimitsFromType(sharedBaseLengthLimits).y);
                    }
                    break;

                case 2:
                    if (!Input.GetKey(keyTipWidth))
                    {
                        state = 0;
                        return;
                    }
                    if (Input.GetKey(keyLeading) && !isCtrlSrf)
                    {
                        sharedEdgeWidthLeadingTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.up);
                        //sharedEdgeWidthLeadingTip = Mathf.Clamp(sharedEdgeWidthLeadingTip, GetLimitsFromType(sharedEdgeWidthLimits).x, GetLimitsFromType(sharedEdgeWidthLimits).y);
                        float tipThicknessCatched = sharedBaseThicknessTip;
                        sharedBaseThicknessTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponent<Camera>().transform.right, -part.transform.forward) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponent<Camera>().transform.up, part.transform.forward * (part.isMirrored ? 1 : -1));
                        sharedEdgeWidthLeadingTip *= sharedBaseThicknessTip / tipThicknessCatched;
                        sharedEdgeWidthTrailingTip *= sharedBaseThicknessTip / tipThicknessCatched;

                        sharedBaseThicknessTip = Mathf.Clamp(sharedBaseThicknessTip, sharedBaseThicknessLimits.x, sharedBaseThicknessLimits.y);


                        sharedEdgeWidthLeadingTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.up);
                        sharedEdgeWidthLeadingTip = Mathf.Clamp(sharedEdgeWidthLeadingTip, GetLimitsFromType(sharedEdgeWidthLimits).x, GetLimitsFromType(sharedEdgeWidthLimits).y);

                    }
                    else if (Input.GetKey(keyTrailing))
                    {
                        sharedEdgeWidthTrailingTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, -part.transform.up);
                        sharedEdgeWidthTrailingTip = Mathf.Clamp(sharedEdgeWidthTrailingTip, GetLimitsFromType(sharedEdgeWidthLimits).x, GetLimitsFromType(sharedEdgeWidthLimits).y);
                    }
                    else
                    {
                        sharedBaseWidthTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, -part.transform.up);
                        sharedBaseWidthTip = Mathf.Clamp(sharedBaseWidthTip, GetLimitsFromType(sharedBaseWidthTipLimits).x, GetLimitsFromType(sharedBaseWidthTipLimits).y);
                        sharedBaseThicknessTip += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.forward) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.forward * (part.isMirrored ? 1 : -1));
                        sharedBaseThicknessTip = Mathf.Clamp(sharedBaseThicknessTip, sharedBaseThicknessLimits.x, sharedBaseThicknessLimits.y);
                    }
                    break;

                case 3:
                    if (!Input.GetKey(keyRootWidth))
                    {
                        state = 0;
                        return;
                    }
                    if (Input.GetKey(keyLeading) && !isCtrlSrf)
                    {
                        sharedEdgeWidthLeadingRoot += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.up);
                        sharedEdgeWidthLeadingRoot = Mathf.Clamp(sharedEdgeWidthLeadingRoot, 0.04f, Mathf.Infinity);
                        //sharedEdgeWidthLeadingRoot = Mathf.Clamp(sharedEdgeWidthLeadingRoot, GetLimitsFromType(sharedEdgeWidthLimits).x, GetLimitsFromType(sharedEdgeWidthLimits).y);
                    }
                    else if (Input.GetKey(keyTrailing))
                    {
                        sharedEdgeWidthTrailingRoot += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, -part.transform.up);
                        sharedEdgeWidthTrailingRoot = Mathf.Clamp(sharedEdgeWidthTrailingRoot, 0.04f, Mathf.Infinity);
                        //sharedEdgeWidthTrailingRoot = Mathf.Clamp(sharedEdgeWidthTrailingRoot, GetLimitsFromType(sharedEdgeWidthLimits).x, GetLimitsFromType(sharedEdgeWidthLimits).y);
                    }
                    else
                    {
                        sharedBaseWidthRoot += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.up) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, -part.transform.up);
                        sharedBaseWidthRoot = Mathf.Clamp(sharedBaseWidthRoot, GetLimitsFromType(sharedBaseWidthRootLimits).x, GetLimitsFromType(sharedBaseWidthRootLimits).y);
                        sharedBaseThicknessRoot += diff.x * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.right, -part.transform.forward) + diff.y * Vector3.Dot(EditorCamera.Instance.GetComponentCached<Camera>(ref editorCam).transform.up, part.transform.forward * (part.isMirrored ? 1 : -1));
                        sharedBaseThicknessRoot = Mathf.Clamp(sharedBaseThicknessRoot, sharedBaseThicknessLimits.x, sharedBaseThicknessLimits.y);
                    }
                    break;
            }
        }

        private void UpdateUI()
        {

            if (uiEditModeTimeout && uiInstanceIDTarget == 0)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logPropertyWindow)
                {
                    DebugLogWithID("UpdateUI", "Window timeout was left active on scene reload, resetting the window state");
                }

                StopWindowTimeout();
            }

            if (uiInstanceIDLocal != uiInstanceIDTarget)
            {
                return;
            }

            if (uiEditModeTimeout)
            {
                uiEditModeTimer += Time.deltaTime;
                if (uiEditModeTimer > uiEditModeTimeoutDuration)
                {
                    StopWindowTimeout();
                }
            }
            else if (uiEditMode)
            {
                if (Input.GetKeyDown(uiKeyCodeEdit))
                {
                    ExitEditMode();
                }
                else
                {
                    bool cursorInGUI = UIUtility.uiRectWindowEditor.Contains(UIUtility.GetMousePos());
                    if (!cursorInGUI && Input.GetKeyDown(KeyCode.Mouse0))
                    {
                        StaticWingGlobals.CheckHandleLayers();
                        if (Physics.Raycast(EditorLogic.fetch.editorCamera.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 200, 1 << 2))
                        {
                            if (hit.collider.name.StartsWith("handle") || hit.collider.name.StartsWith("ctrlHandle"))
                            {
                                hit.collider.transform.GetComponent<EditorHandle>().OnMouseOver();
                                BackupProperties();
                            }
                        }
                        else
                            ExitEditMode();
                    }
                }
            }
        }

        private void CheckAllFieldValues(out bool geometryUpdate, out bool aeroUpdate)
        {
            geometryUpdate = aeroUpdate = false;

            // all the fields that affect aero
            geometryUpdate |= CheckFieldValue(sharedBaseLength, ref sharedBaseLengthCached);
            geometryUpdate |= CheckFieldValue(sharedBaseWidthRoot, ref sharedBaseWidthRootCached);
            geometryUpdate |= CheckFieldValue(sharedBaseWidthTip, ref sharedBaseWidthTipCached);
            geometryUpdate |= CheckFieldValue(sharedBaseThicknessRoot, ref sharedBaseThicknessRootCached);
            geometryUpdate |= CheckFieldValue(sharedBaseThicknessTip, ref sharedBaseThicknessTipCached);
            geometryUpdate |= CheckFieldValue(sharedBaseOffsetRoot, ref sharedBaseOffsetRootCached);
            geometryUpdate |= CheckFieldValue(sharedBaseOffsetTip, ref sharedBaseOffsetTipCached);

            geometryUpdate |= CheckFieldValue(sharedEdgeTypeTrailing, ref sharedEdgeTypeTrailingCached);
            geometryUpdate |= CheckFieldValue(sharedEdgeWidthTrailingRoot, ref sharedEdgeWidthTrailingRootCached);
            geometryUpdate |= CheckFieldValue(sharedEdgeWidthTrailingTip, ref sharedEdgeWidthTrailingTipCached);

            geometryUpdate |= CheckFieldValue(sharedEdgeTypeLeading, ref sharedEdgeTypeLeadingCached);
            geometryUpdate |= CheckFieldValue(sharedEdgeWidthLeadingRoot, ref sharedEdgeWidthLeadingRootCached);
            geometryUpdate |= CheckFieldValue(sharedEdgeWidthLeadingTip, ref sharedEdgeWidthLeadingTipCached);

            aeroUpdate |= geometryUpdate;

            // all the fields that have no aero effects

            geometryUpdate |= CheckFieldValue(sharedArmorRatio, ref sharedArmorRatioCached);
            geometryUpdate |= CheckFieldValue(sharedMaterialST, ref sharedMaterialSTCached);
            geometryUpdate |= CheckFieldValue(sharedColorSTOpacity, ref sharedColorSTOpacityCached);
            geometryUpdate |= CheckFieldValue(sharedColorSTHue, ref sharedColorSTHueCached);
            geometryUpdate |= CheckFieldValue(sharedColorSTSaturation, ref sharedColorSTSaturationCached);
            geometryUpdate |= CheckFieldValue(sharedColorSTBrightness, ref sharedColorSTBrightnessCached);

            geometryUpdate |= CheckFieldValue(sharedMaterialSB, ref sharedMaterialSBCached);
            geometryUpdate |= CheckFieldValue(sharedColorSBOpacity, ref sharedColorSBOpacityCached);
            geometryUpdate |= CheckFieldValue(sharedColorSBHue, ref sharedColorSBHueCached);
            geometryUpdate |= CheckFieldValue(sharedColorSBSaturation, ref sharedColorSBSaturationCached);
            geometryUpdate |= CheckFieldValue(sharedColorSBBrightness, ref sharedColorSBBrightnessCached);

            geometryUpdate |= CheckFieldValue(sharedMaterialET, ref sharedMaterialETCached);
            geometryUpdate |= CheckFieldValue(sharedColorETOpacity, ref sharedColorETOpacityCached);
            geometryUpdate |= CheckFieldValue(sharedColorETHue, ref sharedColorETHueCached);
            geometryUpdate |= CheckFieldValue(sharedColorETSaturation, ref sharedColorETSaturationCached);
            geometryUpdate |= CheckFieldValue(sharedColorETBrightness, ref sharedColorETBrightnessCached);

            geometryUpdate |= CheckFieldValue(sharedMaterialEL, ref sharedMaterialELCached);
            geometryUpdate |= CheckFieldValue(sharedColorELOpacity, ref sharedColorELOpacityCached);
            geometryUpdate |= CheckFieldValue(sharedColorELHue, ref sharedColorELHueCached);
            geometryUpdate |= CheckFieldValue(sharedColorELSaturation, ref sharedColorELSaturationCached);
            geometryUpdate |= CheckFieldValue(sharedColorELBrightness, ref sharedColorELBrightnessCached);
        }

        private bool CheckFieldValue(float fieldValue, ref float fieldCache)
        {
            if (fieldValue != fieldCache)
            {
                if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdate)
                {
                    DebugLogWithID("Update", "Detected value change");
                }

                fieldCache = fieldValue;
                return true;
            }

            return false;
        }

        private void StopWindowTimeout()
        {
            HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = true;
            uiEditModeTimeout = false;
            uiEditModeTimer = 0.0f;
        }

        private void ExitEditMode()
        {
            uiEditMode = false;
            uiEditModeTimeout = true;
            HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().uiAdjustWindow = true;
        }

        private string GetWindowTitle()
        {
            return
                !uiEditMode
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000115")		// #autoLOC_B9_Aerospace_WingStuff_1000115 = Inactive
                : isCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000116")		// #autoLOC_B9_Aerospace_WingStuff_1000116 = Control surface
                : isWingAsCtrlSrf
                    ? Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000117")		// #autoLOC_B9_Aerospace_WingStuff_1000117 = All-moving control surface
                : Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000118");		// #autoLOC_B9_Aerospace_WingStuff_1000118 = Wing
        }

        #region Handle Gizmos by CarnationRED
        private static bool handlesEnabled = false;
        private static bool handlesVisible = true;
        private static float backupsharedBaseLength;
        private static float backupsharedBaseWidthRoot;
        private static float backupsharedBaseWidthTip;
        private static float backupsharedBaseOffsetRoot;
        private static float backupsharedBaseOffsetTip;

        public void BackupProperties()
        {
            backupsharedBaseLength = sharedBaseLength;
            backupsharedBaseWidthRoot = sharedBaseWidthRoot;
            backupsharedBaseWidthTip = sharedBaseWidthTip;
            backupsharedBaseOffsetRoot = sharedBaseOffsetRoot;
            backupsharedBaseOffsetTip = sharedBaseOffsetTip;
        }
        /// <summary>
        /// How sensitive the mouse is
        /// </summary>
        float MouseSensitivity => (float)HighLogic.CurrentGame.Parameters.CustomParams<WPSettings>().mouseSensitivity;

        private void UpdateHandleGizmos()
        {
            // Undoing in the Editor destroys all the handle gizmos.
            if (StaticWingGlobals.handlesRoot == null)
            {
                if (StaticWingGlobals.loadingAssets) return;
                Debug.Log($"[B9PW] Reloading Bundle Assets");
                StartCoroutine(StaticWingGlobals.Instance.LoadBundleAssets());
            }
            if (!uiEditMode)
            {
                if (handlesEnabled)
                    DetachHandles();
                return;
            }

            //Attach handles to current wing
            if (handlesVisible && (!handlesEnabled || Input.GetKeyDown(uiKeyCodeEdit)) && part.GetInstanceID() == uiInstanceIDTarget)
            {
                if (StaticWingGlobals.handlesRoot.transform != null)
                    AttachHandles();
                else
                    Debug.Log("WingProcedural, StaticWingGlobals.handlesRoot.transform is null");
            }

            #region Update positions
            if (!isCtrlSrf)
            {
                StaticWingGlobals.handleLength.transform.localPosition = new Vector3(sharedBaseLength, -sharedBaseOffsetTip, 0);
                float halfTipWidth = sharedBaseWidthTip * .5f;
                StaticWingGlobals.handleWidthTipFront.transform.localPosition = new Vector3(sharedBaseLength, -sharedBaseOffsetTip + halfTipWidth, 0);
                StaticWingGlobals.handleWidthTipBack.transform.localPosition = new Vector3(sharedBaseLength, -sharedBaseOffsetTip - halfTipWidth, 0);
                float halfRootWidth = sharedBaseWidthRoot * .5f;
                StaticWingGlobals.handleWidthRootFront.transform.localPosition = new Vector3(0, sharedBaseOffsetRoot + halfRootWidth, 0);
                StaticWingGlobals.handleWidthRootBack.transform.localPosition = new Vector3(0, sharedBaseOffsetRoot - halfRootWidth, 0);
                StaticWingGlobals.handleLeadingRoot.transform.localPosition = new Vector3(0, sharedBaseOffsetRoot + halfRootWidth + sharedEdgeWidthLeadingRoot, 0);
                StaticWingGlobals.handleLeadingTip.transform.localPosition = new Vector3(sharedBaseLength, -sharedBaseOffsetTip + halfTipWidth + sharedEdgeWidthLeadingTip, 0);
                StaticWingGlobals.handleTrailingRoot.transform.localPosition = new Vector3(0, sharedBaseOffsetRoot - halfRootWidth - sharedEdgeWidthTrailingRoot, 0);
                StaticWingGlobals.handleTrailingTip.transform.localPosition = new Vector3(sharedBaseLength, -sharedBaseOffsetTip - halfTipWidth - sharedEdgeWidthTrailingTip, 0);
            }
            else
            {
                var halfLength = sharedBaseLength * .5f;
                StaticWingGlobals.ctrlHandleLength1.transform.localPosition = new Vector3(-halfLength, 0, 0);
                StaticWingGlobals.ctrlHandleLength2.transform.localPosition = new Vector3(halfLength, 0, 0);
                StaticWingGlobals.ctrlHandleRootWidthOffset.transform.localPosition = new Vector3(halfLength - sharedBaseWidthRoot * sharedBaseOffsetRoot, -sharedBaseWidthRoot, 0);
                StaticWingGlobals.ctrlHandleTipWidthOffset.transform.localPosition = new Vector3(-halfLength - sharedBaseWidthTip * sharedBaseOffsetTip, -sharedBaseWidthTip, 0);
                StaticWingGlobals.ctrlHandleTrailingRoot.transform.localPosition = new Vector3(halfLength - sharedBaseOffsetRoot * (sharedBaseWidthRoot + sharedEdgeWidthTrailingRoot), -(sharedBaseWidthRoot + sharedEdgeWidthTrailingRoot), 0);
                StaticWingGlobals.ctrlHandleTrailingTip.transform.localPosition = new Vector3(-halfLength - sharedBaseOffsetTip * (sharedBaseWidthTip + sharedEdgeWidthTrailingTip), -(sharedBaseWidthTip + sharedEdgeWidthTrailingTip), 0);
            }
            #endregion

            if (EditorHandle.AnyHandleDragging)
            {
                EditorHandle draggingHandle = EditorHandle.draggingHandle;

                var lastFieldID = 0;
                var prev_sharedBaseLength = sharedBaseLength;
                var prev_sharedEdgeWidthLeadingRoot = sharedEdgeWidthLeadingRoot;
                var prev_sharedEdgeWidthLeadingTip = sharedEdgeWidthLeadingTip;
                var prev_sharedEdgeWidthTrailingRoot = sharedEdgeWidthTrailingRoot;
                var prev_sharedEdgeWidthTrailingTip = sharedEdgeWidthTrailingTip;
                var prev_sharedBaseWidthRoot = sharedBaseWidthRoot;
                var prev_sharedBaseWidthTip = sharedBaseWidthTip;
                if (!isCtrlSrf)
                {
                    switch (draggingHandle.name)
                    {
                        case "handleLength":
                            sharedBaseLength = backupsharedBaseLength + draggingHandle.LockDeltaAxisX;
                            sharedBaseOffsetTip = backupsharedBaseOffsetTip - draggingHandle.LockDeltaAxisY;
                            break;
                        case "handleLeadingRoot": sharedEdgeWidthLeadingRoot += draggingHandle.axisY * MouseSensitivity; break;
                        case "handleLeadingTip": sharedEdgeWidthLeadingTip += draggingHandle.axisY * MouseSensitivity; break;
                        case "handleTrailingRoot": sharedEdgeWidthTrailingRoot += draggingHandle.axisY * MouseSensitivity; break;
                        case "handleTrailingTip": sharedEdgeWidthTrailingTip += draggingHandle.axisY * MouseSensitivity; break;
                        case "handleWidthRootFront":
                            sharedBaseWidthRoot -= draggingHandle.axisY * MouseSensitivity;
                            sharedBaseOffsetRoot -= draggingHandle.axisY * MouseSensitivity * .5f;
                            break;
                        case "handleWidthRootBack":
                            sharedBaseWidthRoot += draggingHandle.axisY * MouseSensitivity;
                            sharedBaseOffsetRoot -= draggingHandle.axisY * MouseSensitivity * .5f;
                            break;
                        case "handleWidthTipFront":
                            sharedBaseWidthTip += draggingHandle.axisY * MouseSensitivity;
                            sharedBaseOffsetTip -= draggingHandle.axisY * MouseSensitivity * .5f;
                            break;
                        case "handleWidthTipBack":
                            sharedBaseWidthTip -= draggingHandle.axisY * MouseSensitivity;
                            sharedBaseOffsetTip -= draggingHandle.axisY * MouseSensitivity * .5f;
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    switch (draggingHandle.name)
                    {
                        case "ctrlHandleLength1": sharedBaseLength = backupsharedBaseLength - draggingHandle.LockDeltaAxisY; break;
                        case "ctrlHandleLength2": sharedBaseLength = backupsharedBaseLength + draggingHandle.LockDeltaAxisY; break;
                        case "ctrlHandleRootWidthOffset": sharedBaseWidthRoot = backupsharedBaseWidthRoot - draggingHandle.LockDeltaAxisY; sharedBaseOffsetRoot = backupsharedBaseOffsetRoot + (!isMirrored && isCtrlSrf && !isWingAsCtrlSrf ? 1f : -1f) * draggingHandle.LockDeltaAxisX * .5F; break;
                        case "ctrlHandleTipWidthOffset": sharedBaseWidthTip = backupsharedBaseWidthTip + draggingHandle.LockDeltaAxisY; sharedBaseOffsetTip = backupsharedBaseOffsetTip + (!isMirrored && isCtrlSrf && !isWingAsCtrlSrf ? -1f : 1f) * draggingHandle.LockDeltaAxisX * .5F; break;
                        case "ctrlHandleTrailingRoot": sharedEdgeWidthTrailingRoot += draggingHandle.axisY * MouseSensitivity; break;
                        case "ctrlHandleTrailingTip": sharedEdgeWidthTrailingTip += draggingHandle.axisY * MouseSensitivity; break;
                        default: break;
                    }
                }

                sharedBaseLength = Mathf.Clamp(sharedBaseLength, GetLimitsFromType(sharedBaseLengthLimits).x, GetLimitsFromType(sharedBaseLengthLimits).y);

                sharedEdgeWidthLeadingRoot = sharedEdgeWidthLeadingRoot > 0 ? sharedEdgeWidthLeadingRoot : 0;
                sharedEdgeWidthLeadingTip = sharedEdgeWidthLeadingTip > 0 ? sharedEdgeWidthLeadingTip : 0;
                sharedEdgeWidthTrailingRoot = sharedEdgeWidthTrailingRoot > 0 ? sharedEdgeWidthTrailingRoot : 0;
                sharedEdgeWidthTrailingTip = sharedEdgeWidthTrailingTip > 0 ? sharedEdgeWidthTrailingTip : 0;
                sharedBaseWidthRoot = Mathf.Clamp(sharedBaseWidthRoot, GetLimitsFromType(sharedBaseWidthRootLimits).x, GetLimitsFromType(sharedBaseWidthRootLimits).y);
                sharedBaseWidthTip = Mathf.Clamp(sharedBaseWidthTip, GetLimitsFromType(sharedBaseWidthTipLimits).x, GetLimitsFromType(sharedBaseWidthTipLimits).y);


                if (prev_sharedBaseLength != sharedBaseLength)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000148"); lastFieldID = 0; }		// #autoLOC_B9_Aerospace_WingStuff_1000148 = Length
                else if (prev_sharedEdgeWidthLeadingRoot != sharedEdgeWidthLeadingRoot)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000149"); lastFieldID = 8; }		// #autoLOC_B9_Aerospace_WingStuff_1000149 = Leading Edge Root Width
                else if (prev_sharedEdgeWidthLeadingTip != sharedEdgeWidthLeadingTip)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000150"); lastFieldID = 9; }		// #autoLOC_B9_Aerospace_WingStuff_1000150 = Leading Edge Tip Width
                else if (prev_sharedEdgeWidthTrailingRoot != sharedEdgeWidthTrailingRoot)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000151"); lastFieldID = 11; }		// #autoLOC_B9_Aerospace_WingStuff_1000151 = Trailing Leading Edge Root Width
                else if (prev_sharedEdgeWidthTrailingTip != sharedEdgeWidthTrailingTip)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000152"); lastFieldID = 12; }		// #autoLOC_B9_Aerospace_WingStuff_1000152 = Trailing Leading Edge Tip Width
                else if (prev_sharedBaseWidthRoot != sharedBaseWidthRoot)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000153"); lastFieldID = 1; }		// #autoLOC_B9_Aerospace_WingStuff_1000153 = Root Width
                else if (prev_sharedBaseWidthTip != sharedBaseWidthTip)
                { uiLastFieldName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000154"); lastFieldID = 2; }		// #autoLOC_B9_Aerospace_WingStuff_1000154 = Tip Width
                uiLastFieldTooltip = UpdateTooltipText(lastFieldID);

                // show/hide hinge position indicator
                if (!isCtrlSrf && isWingAsCtrlSrf)
                {
                    StaticWingGlobals.hingeIndicator.SetActive(sharedBaseOffsetRoot != 0);
                }
            }
        }

        private void DetachHandles()
        {
            StaticWingGlobals.handlesRoot.transform.SetParent(null, false);
            StaticWingGlobals.handlesRoot.transform.localScale = Vector3.one;
            StaticWingGlobals.handlesRoot.SetActive(false);
            handlesEnabled = false;
            if (EditorHandle.AnyHandleDragging) EditorHandle.draggingHandle.dragging = false;
            DontDestroyOnLoad(StaticWingGlobals.handlesRoot);
        }
        private void AttachHandles()
        {
            StaticWingGlobals.handlesRoot.transform.SetParent(part.transform, false);
            StaticWingGlobals.handlesRoot.transform.localScale = (!isMirrored && isCtrlSrf && !isWingAsCtrlSrf) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            StaticWingGlobals.handlesRoot.SetActive(true);
            StaticWingGlobals.normalHandles.SetActive(!isCtrlSrf);
            StaticWingGlobals.ctrlSurfHandles.SetActive(isCtrlSrf);
            StaticWingGlobals.hingeIndicator.SetActive(!isCtrlSrf && isWingAsCtrlSrf && sharedBaseOffsetRoot != 0);
            handlesEnabled = true;
        }
        #endregion

        #endregion Alternative UI/input

        #region Coloration

        // XYZ
        // HSB
        // RGB

        private Color GetVertexColor(int side)
        {
            return ColorHSBToRGB(
                side == 0
                    ? new Vector4(sharedColorSTHue, sharedColorSTSaturation, sharedColorSTBrightness, sharedColorSTOpacity)
                : side == 1
                    ? new Vector4(sharedColorSBHue, sharedColorSBSaturation, sharedColorSBBrightness, sharedColorSBOpacity)
                : side == 2
                    ? new Vector4(sharedColorETHue, sharedColorETSaturation, sharedColorETBrightness, sharedColorETOpacity)
                : new Vector4(sharedColorELHue, sharedColorELSaturation, sharedColorELBrightness, sharedColorELOpacity)
            );
        }

        private Vector2 GetVertexUV2(float selectedLayer)
        {
            return selectedLayer == 0 ? new Vector2(0f, 1f) : new Vector2((selectedLayer - 1f) / 3f, 0f);
        }

        private Color ColorHSBToRGB(Vector4 hsbColor)
        {
            float r = hsbColor.z;
            float g = hsbColor.z;
            float b = hsbColor.z;

            if (hsbColor.y != 0)
            {
                float max = hsbColor.z;
                float dif = hsbColor.z * hsbColor.y;
                float min = hsbColor.z - dif;
                float h = hsbColor.x * 360f;
                if (h < 60f)
                {
                    r = max;
                    g = h * dif / 60f + min;
                    b = min;
                }
                else if (h < 120f)
                {
                    r = -(h - 120f) * dif / 60f + min;
                    g = max;
                    b = min;
                }
                else if (h < 180f)
                {
                    r = min;
                    g = max;
                    b = (h - 120f) * dif / 60f + min;
                }
                else if (h < 240f)
                {
                    r = min;
                    g = -(h - 240f) * dif / 60f + min;
                    b = max;
                }
                else if (h < 300f)
                {
                    r = (h - 240f) * dif / 60f + min;
                    g = min;
                    b = max;
                }
                else if (h <= 360f)
                {
                    r = max;
                    g = min;
                    b = -(h - 360f) * dif / 60 + min;
                }
                else
                {
                    r = 0;
                    g = 0;
                    b = 0;
                }
            }
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), hsbColor.w);
        }

        #endregion Coloration

        #region Resources

        // Original code by Snjo
        // Modified to remove config support and string parsing and to add support for arbitrary volumes
        // Further modified to support custom configs

        public bool fuelDisplayCurrentTankCost = false;
        public bool fuelShowInfo = false;

        [KSPField(isPersistant = true)]
        public int fuelSelectedTankSetup = 0;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Added cost")]
        public float fuelAddedCost = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Dry mass")]
        public float fuelDryMassInfo = 0f;

        /// <summary>
        /// Called from setup (part of Start() for editor and flight)
        /// </summary>
        private void FuelStart()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFuel)
            {
                DebugLogWithID("FuelStart", "Started");
            }

            if (!(CanBeFueled && UseStockFuel))
            {
                return;
            }

            if (HighLogic.LoadedSceneIsEditor && fuelSelectedTankSetup < 0)
            {
                fuelSelectedTankSetup = 0;
                FuelTankTypeChanged();
            }
        }

        /// <summary>
        /// wing geometry changed, update fuel volumes
        /// </summary>
        public void FuelVolumeChanged()
        {
            if (!CanBeFueled)
            {
                return;
            }

            aeroStatVolume = 0.7f * sharedBaseLength * (sharedBaseWidthRoot + sharedBaseWidthTip) * (sharedBaseThicknessRoot + sharedBaseThicknessTip) / 4; // fudgeFactor * length * average thickness * average width
                                                                                                                                                            // no need to worry about symmetry as all symmetric parts will experience the volume change
            if (UseStockFuel)
            {
                for (int i = 0; i < part.Resources.Count; ++i)
                {
                    PartResource res = part.Resources[i];
                    if (StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources.TryGetValue(res.resourceName, out WingTankResource wres))
                    {
                        double fillPct = res.maxAmount > 0 ? res.amount / res.maxAmount : 1.0;
                        res.maxAmount = aeroStatVolume * StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources[res.resourceName].unitsPerVolume;
                        res.amount = res.maxAmount * fillPct;
                    }
                }
                UpdateWindow();
            }
            else
            {
                FuelSetResources(); // for MFT/RF/CC.
            }
        }

        /// <summary>
        /// fuel type changed, re set wing fuel configurations
        /// </summary>
        public void FuelTankTypeChanged()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFuel)
            {
                DebugLogWithID("FuelAssignResourcesToPart", "Started");
            }

            FuelSetResources();
            foreach (Part p in part.symmetryCounterparts)
            {
                if (p == null) // fixes nullref caused by removing mirror sym while hovering over attach location
                {
                    continue;
                }

                WingProcedural wing = FirstOfTypeOrDefault<WingProcedural>(p.Modules);
                if (wing != null)
                {
                    wing.fuelSelectedTankSetup = fuelSelectedTankSetup;
                    wing.FuelSetResources();
                }
            }

            UpdateWindow();
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        /// <summary>
        /// lifting vs structural changed, re set configurations
        /// </summary>
        public void LiftStructuralTypeChanged()
        {
            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logUpdateGeometry)
            {
                DebugLogWithID("UpdateGeometry", "Lifting Surface Type Change | Finished");
            }

            WingSetLiftingSurface();
            foreach (Part p in part.symmetryCounterparts)
            {
                if (p == null) // fixes nullref caused by removing mirror sym while hovering over attach location
                {
                    continue;
                }

                WingProcedural wing = FirstOfTypeOrDefault<WingProcedural>(p.Modules);
                if (wing != null)
                {
                    wing.aeroIsLiftingSurface = aeroIsLiftingSurface;
                    wing.WingSetLiftingSurface();
                }
            }

            UpdateWindow();
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        /// <summary>
        /// Updates wing lift settings
        /// </summary>
        public void WingSetLiftingSurface()
        {
            if (!(CanBeFueled && HighLogic.LoadedSceneIsEditor) || assemblyFARUsed)
            {
                return;
            }

            CalculateAerodynamicValues();

            if (aeroIsLiftingSurface)
            {
                Events["ToggleLiftConfiguration"].guiName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000163");//Surface Config: Lifting
            }
            else
            {
                Events["ToggleLiftConfiguration"].guiName = Localizer.Format("#autoLOC_B9_Aerospace_WingStuff_1000164");//Surface Config: Not Lifting
            }
        }

        /// <summary>
        /// Updates part.Resources to match the changes or notify MFT/RF if applicable
        /// </summary>
        public void FuelSetResources()
        {
            if (!(CanBeFueled && HighLogic.LoadedSceneIsEditor))
            {
                return;
            }

            if (HighLogic.CurrentGame.Parameters.CustomParams<WPDebug>().logFuel)
            {
                DebugLogWithID("FuelSetupTankInPart", "Started");
            }

            if (!UseStockFuel)
            {
                // send public event OnPartVolumeChanged, like ProceduralParts does
                // MFT/RT also support this event
                BaseEventDetails data = new BaseEventDetails(BaseEventDetails.Sender.USER);
                // PP uses two volume types: Tankage for resources and Habitation
                data.Set<string>("volName", "Tankage");
                // aeroStatVolume should be in m3
                // to change the meaning for MFT, use ModuleFuelTanks.tankVolumeConversion field in part cfg
                // for RF this field defaults to 1000, so nothing needs to be done
                data.Set<double>("newTotalVolume", aeroStatVolume);
                part.SendEvent("OnPartVolumeChanged", data, 0);
            }
            else
            {
                for (int i = part.Resources.Count - 1; i >= 0; --i)
                {
                    part.Resources.Remove(part.Resources[i]);
                }

                foreach (KeyValuePair<string, WingTankResource> kvp in StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources)
                {
                    ConfigNode newResourceNode = new ConfigNode("RESOURCE");
                    newResourceNode.AddValue("name", kvp.Value.resource.name);
                    newResourceNode.AddValue("amount", kvp.Value.unitsPerVolume * aeroStatVolume);
                    newResourceNode.AddValue("maxAmount", kvp.Value.unitsPerVolume * aeroStatVolume);
                    part.AddResource(newResourceNode);
                }
                fuelAddedCost = FuelGetAddedCost();
            }
        }

        /// <summary>
        /// returns cost of max amount of fuel that the tanks can carry with the current loadout
        /// </summary>
        /// <returns></returns>
        private float FuelGetAddedCost()
        {
            float result = 0f;
            if (CanBeFueled && UseStockFuel && fuelSelectedTankSetup < StaticWingGlobals.wingTankConfigurations.Count && fuelSelectedTankSetup >= 0)
            {
                foreach (KeyValuePair<string, WingTankResource> kvp in StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources)
                {
                    result += kvp.Value.resource.unitCost * aeroStatVolume * kvp.Value.unitsPerVolume;
                }
            }
            return result;
        }

        /// <summary>
        /// returns a string containing an abreviation of the current fuels and the number of units of each. eg LFO (360/420)
        /// </summary>
        private string FuelGUIGetConfigDesc()
        {
            if (fuelSelectedTankSetup == -1 || StaticWingGlobals.wingTankConfigurations.Count == 0)
            {
                return "Invalid";
            }
            else
            {
                if (StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources.Count != 0)
                {
                    string units = StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].GUIName + " (";
                    foreach (KeyValuePair<string, WingTankResource> kvp in StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].resources)
                    {
                        units += " " + (kvp.Value.unitsPerVolume * aeroStatVolume).ToString("G5") + " /";
                    }
                    //units = units.Substring(0, units.Length - 1);
                    return units.Substring(0, units.Length - 1) + ") ";
                }
                return StaticWingGlobals.wingTankConfigurations[fuelSelectedTankSetup].GUIName + " ";
            }
        }

        public bool CanBeFueled => !isCtrlSrf && !isWingAsCtrlSrf && !isPanel;
        public bool UseStockFuel => !(assemblyRFUsed || assemblyMFTUsed || moduleCCUsed);

        #endregion Resources

        #region Interfaces

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            return FuelGetAddedCost() + aeroUICost - part.partInfo.cost;
        }

        public ModifierChangeWhen GetModuleCostChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return assemblyFARUsed ? 0 + sharedArmorRatio * (aeroUIMass - part.partInfo.partPrefab.mass) / 100 : (aeroUIMass - part.partInfo.partPrefab.mass) * (100 + sharedArmorRatio) / 100;
        }

        public ModifierChangeWhen GetModuleMassChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }

        public Vector3 GetModuleSize(Vector3 defaultSize, ModifierStagingSituation sit)
        {
            return Vector3.zero;
        }

        public ModifierChangeWhen GetModuleSizeChangeWhen()
        {
            return ModifierChangeWhen.FIXED;
        }

        #endregion Interfaces

        public T FirstOfTypeOrDefault<T>(PartModuleList moduleList) where T : PartModule
        {
            foreach (PartModule pm in moduleList)
            {
                if (pm is T t)
                {
                    return t;
                }
            }
            return default;
        }
        #region Dump state

        public void DumpState()
        {
            string report = "State report on part " + this.GetInstanceID() + ":\n\n";
            Type type = this.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            List<string> fieldNames = fields.Select(field => field.Name).ToList();
            List<object> fieldValues = fields.Select(field => field.GetValue(this)).ToList();
            if (fieldNames.Count == fieldValues.Count && fieldNames.Count == fields.Length)
            {
                for (int i = 0; i < fields.Length; ++i)
                {
                    if (!string.IsNullOrEmpty(fieldNames[i]))
                    {
                        if (fieldValues[i] != null) report += fieldNames[i] + ": " + fieldValues[i].ToString() + "\n";
                        else report += fieldNames[i] + ": null\n";
                    }
                    else report += "Field " + i.ToString() + " name not available\n";
                }
            }
            else report += "Field info size mismatch, list can't be printed";
            Debug.Log(report);
        }

        public void DumpExecutionTimes()
        {
            Debug.Log("Dumping execution time report, message list contains " + debugMessageList.Count);
            string report = "Execution time report on part " + this.GetInstanceID() + ":\n\n";
            int count = debugMessageList.Count;
            for (int i = 0; i < count; ++i)
            {
                report += "I: " + debugMessageList[i].interval + "\n> M: " + (debugMessageList[i].message.Length <= 140 ? (debugMessageList[i].message) : (debugMessageList[i].message.Substring(0, 135) + "(...)")) + "\n";
            }
            Debug.Log(report);
        }
        #endregion
    }
}
