﻿namespace Models.WaterModel
{
    using APSIM.Shared.Utilities;
    using Interfaces;
    using Models.Core;
    using Soils;
    using System;
    using System.Globalization;
    using System.Xml.Serialization;

    /// <summary>
    /// The SoilWater module is a cascading water balance model that owes much to its precursors in 
    /// CERES (Jones and Kiniry, 1986) and PERFECT(Littleboy et al, 1992). 
    /// The algorithms for redistribution of water throughout the soil profile have been inherited from 
    /// the CERES family of models.
    ///
    /// The water characteristics of the soil are specified in terms of the lower limit (ll15), 
    /// drained upper limit(dul) and saturated(sat) volumetric water contents. Water movement is 
    /// described using separate algorithms for saturated or unsaturated flow. It is notable that 
    /// redistribution of solutes, such as nitrate- and urea-N, is carried out in this module.
    ///
    /// Modifications adopted from PERFECT include:
    /// * the effects of surface residues and crop cover on modifying runoff and reducing potential soil evaporation,
    /// * small rainfall events are lost as first stage evaporation rather than by the slower process of second stage evaporation, and
    /// * specification of the second stage evaporation coefficient(cona) as an input parameter, providing more flexibility for describing differences in long term soil drying due to soil texture and environmental effects.
    ///
    /// The module is interfaced with SurfaceOrganicMatter and crop modules so that simulation of the soil water balance 
    /// responds to change in the status of surface residues and crop cover(via tillage, decomposition and crop growth).
    ///
    /// Enhancements beyond CERES and PERFECT include:
    /// * the specification of swcon for each layer, being the proportion of soil water above dul that drains in one day
    /// * isolation from the code of the coefficients determining diffusivity as a function of soil water
    ///   (used in calculating unsaturated flow).Choice of diffusivity coefficients more appropriate for soil type have been found to improve model performance.
    /// * unsaturated flow is permitted to move water between adjacent soil layers until some nominated gradient in 
    ///   soil water content is achieved, thereby accounting for the effect of gravity on the fully drained soil water profile.
    ///
    /// SoilWater is called by APSIM on a daily basis, and typical of such models, the various processes are calculated consecutively. 
    /// This contrasts with models such as SWIM that solve simultaneously a set of differential equations that describe the flow processes.
    /// </summary>
    [ValidParent(ParentType = typeof(Soil))]
    [ViewName("UserInterface.Views.ProfileView")]
    [PresenterName("UserInterface.Presenters.ProfilePresenter")]
    [Serializable]
    public class WaterBalance : Model, ISoilWater
    {
        // --- Links -------------------------------------------------------------------------

        /// <summary>Link to the soil properties.</summary>
        [Link]
        private Soil soil = null;

        /// <summary>Link to the lateral flow model.</summary>
        [Link]
        private LateralFlowModel lateralFlowModel = null;

        /// <summary>Link to the runoff model.</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private RunoffModel runoffModel = null;

        /// <summary>Link to the saturated flow model.</summary>
        [Link]
        private SaturatedFlowModel saturatedFlow = null;

        /// <summary>Link to the unsaturated flow model.</summary>
        [Link]
        private UnsaturatedFlowModel unsaturatedFlow = null;

        /// <summary>Link to the evaporation model.</summary>
        [Link]
        private EvaporationModel evaporationModel = null;

        /// <summary>Link to the water table model.</summary>
        [Link(Type = LinkType.Child, ByName = true)]
        private WaterTableModel waterTableModel = null;

        /// <summary>A link to a irrigation data.</summary>
        [Link]
        private IIrrigation irrigation = null;

        [Link(ByName = true)]
        ISolute NO3 = null;

        [Link]
        ISolute NH4 = null;

        // --- Settable properties -------------------------------------------------------

        /// <summary>Amount of water in the soil (mm).</summary>
        [XmlIgnore]
        public double[] Water { get; set; }

        /// <summary>Runon (mm).</summary>
        [XmlIgnore]
        public double Runon { get; set; }

        /// <summary>The efficiency (0-1) that solutes move down with water.</summary>
        public double SoluteFluxEfficiency { get; set; } = 1;

        /// <summary>The efficiency (0-1) that solutes move up with water.</summary>
        public double SoluteFlowEfficiency { get; set; } = 1;

        /// <summary> This is set by Microclimate and is rainfall less that intercepted by the canopy and residue components </summary>
        [XmlIgnore]
        public double PotentialInfiltration { get; set; }

        // --- Outputs -------------------------------------------------------------------

        /// <summary>Lateral flow (mm).</summary>
        [XmlIgnore]
        public double[] LateralFlow { get; private set; }

        /// <summary>Runoff (mm).</summary>
        [XmlIgnore]
        public double Runoff { get; private set; }

        /// <summary>Infiltration (mm).</summary>
        [XmlIgnore]
        public double Infiltration { get; private set; }

        /// <summary>Drainage (mm).</summary>
        [XmlIgnore]
        public double Drain { get { return Flux[Flux.Length - 1]; } }

        /// <summary>Evaporation (mm).</summary>
        [XmlIgnore]
        public double Evaporation { get { return evaporationModel.Es; } }

        /// <summary>Water table depth (mm).</summary>
        [XmlIgnore]
        public double WaterTableDepth { get { return waterTableModel.Depth; } }

        /// <summary>Flux. Water moving down (mm).</summary>
        [XmlIgnore]
        public double[] Flux { get; private set; }

        /// <summary>Flow. Water moving up (mm).</summary>
        [XmlIgnore]
        public double[] Flow { get; private set; }

        /// <summary>Gets todays potential runoff (mm).</summary>
        public double PotentialRunoff
        {
            get
            {
                double waterForRunoff = PotentialInfiltration;

                if (irrigation.WillRunoff)
                    waterForRunoff = waterForRunoff + irrigation.IrrigationApplied;

                return waterForRunoff;
            }
        }

        /// <summary>Provides access to the soil properties.</summary>
        public Soil Properties { get { return soil; } }

        ///<summary>Gets or sets soil thickness for each layer (mm)(</summary>
        [XmlIgnore]
        public double[] Thickness => throw new NotImplementedException();

        ///<summary>Gets or sets volumetric soil water content (mm/mm)(</summary>
        [XmlIgnore]
        public double[] SW { get { return MathUtilities.Divide(Water, soil.Thickness); } set { Water = MathUtilities.Multiply(value, soil.Thickness); ; } }

        ///<summary>Gets soil water content (mm)</summary>
        [XmlIgnore]
        public double[] SWmm { get { return Water; } }

        ///<summary>Gets extractable soil water relative to LL15(mm)</summary>
        [XmlIgnore]
        public double[] ESW { get { return MathUtilities.Subtract(Water, soil.LL15mm); } }

        ///<summary>Gets potential evaporation from soil surface (mm)</summary>
        [XmlIgnore]
        public double Eos { get { return evaporationModel.Eos; } }

        /// <summary>Gets the actual (realised) soil water evaporation (mm)</summary>
        [XmlIgnore]
        public double Es { get { return evaporationModel.Es; } }

        /// <summary>Gets potential evapotranspiration of the whole soil-plant system (mm)</summary>
        [XmlIgnore]
        public double Eo { get; set; }

        /// <summary>Gets the amount of water drainage from bottom of profile(mm)</summary>
        [XmlIgnore]
        public double Drainage => throw new NotImplementedException();

        /// <summary>Fraction of incoming radiation reflected from bare soil</summary>
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Caption("Albedo")]
        [Description("Fraction of incoming radiation reflected from bare soil")]
        public double Salb { get; set; }

        /// <summary>Amount of water moving laterally out of the profile (mm)</summary>
        [XmlIgnore]
        public double[] LateralOutflow => throw new NotImplementedException();

        /// <summary>Amount of N leaching as NO3-N from the deepest soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double LeachNO3 => throw new NotImplementedException();

        /// <summary>Amount of N leaching as NH4-N from the deepest soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double LeachNH4 => throw new NotImplementedException();

        /// <summary>Amount of N leaching as urea-N  from the deepest soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double LeachUrea => throw new NotImplementedException();

        /// <summary>Amount of N leaching as NO3 from each soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double[] FlowNO3 { get; private set; }

        /// <summary>Amount of N leaching as NH4 from each soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double[] FlowNH4 => throw new NotImplementedException();

        /// <summary>Amount of N leaching as urea from each soil layer (kg /ha)</summary>
        [XmlIgnore]
        public double[] FlowUrea => throw new NotImplementedException();

        /// <summary> This is set by Microclimate and is rainfall less that intercepted by the canopy and residue components </summary>
        [XmlIgnore]
        public double PrecipitationInterception { get; set; }

        // --- Event handlers ------------------------------------------------------------

        /// <summary>Called when a simulation commences.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            // Set our water to the initial value.
            Water = soil.Initial.SWmm;
        }

        /// <summary>Called by CLOCK to let this model do its water movement.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event data.</param>
        [EventSubscribe("DoSoilWaterMovement")]
        private void OnDoSoilWaterMovement(object sender, EventArgs e)
        {
            // Calculate lateral flow.
            LateralFlow = lateralFlowModel.Values;
            if (LateralFlow != null)
                Water = MathUtilities.Subtract(Water, LateralFlow);

            // Calculate runoff.
            Runoff = runoffModel.Value();

            // Calculate infiltration.
            Infiltration = PotentialInfiltration - Runoff;
            Water[0] = Water[0] + Infiltration;

            // Allow irrigation to infiltrate.
            if (!irrigation.WillRunoff && irrigation.IrrigationApplied > 0)
            {
                int irrigationLayer = soil.LayerIndexOfDepth(Convert.ToInt32(irrigation.Depth, CultureInfo.InvariantCulture));
                Water[irrigationLayer] = irrigation.IrrigationApplied;
                Infiltration += irrigation.IrrigationApplied;

                // DeanH - haven't implemented solutes in irrigation water yet.
                // NO3[irrigationLayer] = irrigation.NO3;
                // NH4[irrigationLayer] = irrigation.NH4;
                // CL[irrigationLayer] = irrigation.Cl;
            }

            // Saturated flow.
            Flux = saturatedFlow.Values;

            // Add backed up water to runoff. 
            Water[0] = Water[0] - saturatedFlow.backedUpSurface;

            // Now reduce the infiltration amount by what backed up.
            Infiltration = Infiltration - saturatedFlow.backedUpSurface;

            // Turn the proportion of the infiltration that backed up into runoff.
            Runoff = Runoff + saturatedFlow.backedUpSurface;

            // Should go to pond if one exists.
            //  pond = Math.Min(Runoff, max_pond);
            MoveDown(Water, Flux);

            double[] NO3Values = NO3.kgha;
            double[] NH4Values = NH4.kgha;

            // Calcualte solute movement down with water.
            SoluteFluxEfficiency = 1;
            double[] NO3Down = CalculateSoluteMovementDown(NO3Values, Water, Flux, SoluteFluxEfficiency);
            //double[] NH4Down = CalculateSoluteMovementDown(NH4Values, Water, Flux, SoluteFluxEfficiency);
            MoveDown(NO3Values, NO3Down);
            //MoveDown(NH4Values, NH4Down);

            double es = evaporationModel.Calculate();
            Water[0] = Water[0] - es;

            Flow = unsaturatedFlow.Values;
            MoveUp(Water, Flow);

            CheckForErrors();

            SoluteFlowEfficiency = 1;
            double waterTableDepth = waterTableModel.Value();
            FlowNO3 = CalculateSoluteMovementUp(NO3Values, Water, Flow, SoluteFlowEfficiency);
            //double[] NH4Up = CalculateSoluteMovementUpDown(NH4.kgha, Water, Flow, SoluteFlowEfficiency);
            MoveUp(NO3Values, FlowNO3);
            //MoveUp(NH4Values, NH4Up);

            // Set deltas
            NO3.SetKgHa(SoluteSetterType.Soil, NO3Values);
            //NH4.SetKgHa(SoluteSetterType.Soil, NH4Values);
        }

        /// <summary>Move water down the profile</summary>
        /// <param name="water">The water values</param>
        /// <param name="flux">The amount to move down</param>
        private static void MoveDown(double[] water, double[] flux)
        {
            for (int i = 0; i < water.Length; i++)
            {
                if (i == 0)
                    water[i] = water[i] - flux[i];
                else
                    water[i] = water[i] + flux[i-1] - flux[i];
            }
        }

        /// <summary>Move water up the profile.</summary>
        /// <param name="water">The water values.</param>
        /// <param name="flow">The amount to move up.</param>
        private static void MoveUp(double[] water, double[] flow)
        {
            for (int i = 0; i < water.Length; i++)
            {
                if (i == 0)
                    water[i] = water[i] + flow[i];
                else
                    water[i] = water[i] + flow[i] - flow[i-1];
            }
        }

        /// <summary>Calculate the solute movement DOWN based on flux.</summary>
        /// <param name="solute"></param>
        /// <param name="water"></param>
        /// <param name="flux"></param>
        /// <param name="efficiency"></param>
        /// <returns></returns>
        private static double[] CalculateSoluteMovementDown(double[] solute, double[] water, double[] flux, double efficiency)
        {
            double[] soluteFlux = new double[solute.Length];
            for (int i = 0; i < solute.Length; i++)
            {
                if (i == 0)
                    soluteFlux[i] = flux[i] * solute[i] / (water[i] + flux[i]);
                else
                    soluteFlux[i] = flux[i] * (solute[i] + soluteFlux[i-1]) / (water[i] + flux[i]);
                    //soluteFlux[i] = (solute[i] + soluteFlux[i-1]) * proportionMoving * efficiency;
            }

            return soluteFlux;
        }

        /// <summary>Calculate the solute movement UP and DOWN based on flow.</summary>
        /// <param name="solute"></param>
        /// <param name="water"></param>
        /// <param name="flux"></param>
        /// <param name="efficiency"></param>
        /// <returns></returns>
        private static double[] CalculateSoluteMovementUpDown(double[] solute, double[] water, double[] flux, double efficiency)
        {
            double[] soluteUp = CalculateSoluteMovementUp(solute, water, flux, efficiency);
            //MoveUp(solute, soluteUp);
            double[] soluteDown = CalculateSoluteMovementDown(solute, water, flux, efficiency);
            return MathUtilities.Subtract(soluteUp, soluteDown);
        }

        /// <summary>Calculate the solute movement UP based on flow.</summary>
        /// <param name="solute"></param>
        /// <param name="water"></param>
        /// <param name="flow"></param>
        /// <param name="efficiency"></param>
        /// <returns></returns>
        private static double[] CalculateSoluteMovementUp(double[] solute, double[] water, double[] flow, double efficiency)
        {
            // flow[i] is the water coming into a layer from the layer below
            double[] soluteFlow = new double[solute.Length];
            for (int i = solute.Length - 1; i > 0; i--)
            {
                if (i == solute.Length - 1)
                    // soluteFlow[i] = 0;?
                    soluteFlow[i] = flow[i] * solute[i] / (water[i] - flow[i]);
                else
                {
                    double initialWater = water[i];
                    double waterComingIn = flow[i];
                    double waterMovingOut = flow[i - 1];
                    double totalWater = initialWater + waterComingIn - waterMovingOut;

                    double initialSolute = solute[i];

                    // soluteFlow[i] is the solutes flowing into this layer from the layer below.
                    // this is the water moving into this layer * solute concentration. That is,
                    // water in this layer * solute in this layer / water in this layer.
                    //
                    // todo: should this be solute[i + 1] because solute concenctration in the water
                    // should actually be the solute concenctration in the water moving into this layer
                    // from the layer below.
                    soluteFlow[i] = flow[i] * solute[i] / totalWater;
                }
            }

            return soluteFlow;
        }

        /// <summary>Checks for soil for errors.</summary>
        private void CheckForErrors()
        {
            const double specific_bd = 2.65;

            double min_sw = 0.0;

            for (int i = 0; i < soil.Thickness.Length; i++)
            {
               double max_sw = 1.0 - MathUtilities.Divide(soil.BD[i], specific_bd, 0.0);  // ie. Total Porosity
                
                if (MathUtilities.IsLessThan(soil.AirDry[i], min_sw))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4})",
                                               " Air dry lower limit of ",
                                               soil.AirDry[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is below acceptable value of ",
                                               min_sw));

                if (MathUtilities.IsLessThan(soil.LL15[i], soil.AirDry[i]))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4})",
                                               " 15 bar lower limit of ",
                                               soil.LL15[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is below air dry value of ",
                                               soil.AirDry[i]));

                if (MathUtilities.IsLessThanOrEqual(soil.DUL[i], soil.LL15[i]))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4})",
                                               " drained upper limit of ",
                                               soil.DUL[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is at or below lower limit of ",
                                               soil.LL15[i]));

                if (MathUtilities.IsLessThanOrEqual(soil.SAT[i], soil.DUL[i]))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4})",
                                               " saturation of ",
                                               soil.SAT[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is at or below drained upper limit of ",
                                               soil.DUL[i]));

                if (MathUtilities.IsGreaterThan(soil.SAT[i], max_sw))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4} {7} {8} {9:G4} {10} {11} {12:G4})",
                                               " saturation of ",
                                               soil.SAT[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is above acceptable value of ",
                                               max_sw,
                                               "\n",
                                               "You must adjust bulk density (bd) to below ",
                                               (1.0 - soil.SAT[i]) * specific_bd,
                                               "\n",
                                               "OR saturation (sat) to below ",
                                               max_sw));

                if (MathUtilities.IsGreaterThan(SW[i], soil.SAT[i]))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4}",
                                               " soil water of ",
                                               SW[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is above saturation of ",
                                               soil.SAT[i]));

                if (MathUtilities.IsLessThan(SW[i], soil.AirDry[i]))
                    throw new Exception(String.Format("({0} {1:G4}) {2} {3} {4} {5} {6:G4}",
                                               " soil water of ",
                                               SW[i],
                                               " in layer ",
                                               i,
                                               "\n",
                                               "         is below air-dry value of ",
                                               soil.AirDry[i]));
            }

        }

        ///<summary>Remove water from the profile</summary>
        public void RemoveWater(double[] amountToRemove)
        {
            Water = MathUtilities.Subtract(Water, amountToRemove);
        }

        /// <summary>Sets the water table.</summary>
        /// <param name="InitialDepth">The initial depth.</param> 
        public void SetWaterTable(double InitialDepth)
        {
            throw new NotImplementedException();
        }

        ///<summary>Perform a reset</summary>
        public void Reset()
        {
            throw new NotImplementedException();
        }

        ///<summary>Perform tillage</summary>
        public void Tillage(TillageType Data)
        {
            throw new NotImplementedException();
        }

        ///<summary>Perform tillage</summary>
        public void Tillage(string tillageType)
        {
            throw new NotImplementedException();
        }
    }
}
