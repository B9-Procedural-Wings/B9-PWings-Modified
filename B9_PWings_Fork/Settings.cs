using System;

namespace WingProcedural
{
    public class WPDebug : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Log Settings"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }

        //public override string DisplaySection => "Graphics";
        public override string Section { get { return "B9 Procedural Wings"; } }
        public override string DisplaySection { get { return "B9 Procedural Wings"; } }

        public override int SectionOrder { get { return 1; } }


        [GameParameters.CustomParameterUI("Enable Aero Logging")]
        public bool logCAV = false;

        [GameParameters.CustomParameterUI("Enable Update Logging")]
        public bool logUpdate = false;

        [GameParameters.CustomParameterUI("Enable Geometry Logging")]
        public bool logUpdateGeometry = false;

        [GameParameters.CustomParameterUI("Enable Material Logging")]
        public bool logUpdateMaterials = false;

        [GameParameters.CustomParameterUI("Enable Mesh ref Logging")]
        public bool logMeshReferences = false;

        [GameParameters.CustomParameterUI("Enable Check Mesh Logging")]
        public bool logCheckMeshFilter = false;

        [GameParameters.CustomParameterUI("Enable Property Logging")]
        public bool logPropertyWindow = false;

        [GameParameters.CustomParameterUI("Enable Flight Setup Logging")]
        public bool logFlightSetup = false;

        [GameParameters.CustomParameterUI("Enable Field Setup Logging")]
        public bool logFieldSetup = false;

        [GameParameters.CustomParameterUI("Enable Fuel Logging")]
        public bool logFuel = false;

        [GameParameters.CustomParameterUI("Enable Limits Logging")]
        public bool logLimits = false;

        [GameParameters.CustomParameterUI("Enable Events Logging")]
        public bool logEvents = false;



        public override bool HasPresets => false;

    }



    public class WPSensitivity : GameParameters.CustomParameterNode
    {
        public override string Title { get { return "Controls"; } }
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.ANY; } }
        public override string Section { get { return "B9 Procedural Wings"; } }
        public override string DisplaySection { get { return "B9 Procedural Wings"; } }

        public override int SectionOrder { get { return 2; } }
        public override bool HasPresets => false;


        [GameParameters.CustomFloatParameterUI("Mouse Sensitivity",
            minValue = 1, maxValue = 8.0f, stepCount = 79, displayFormat = "F1",
            toolTip = "Adjusts how the handles move when being moved by the mouse"
            )]
        public double mouseSensitivity = 4.0f;


    }
}