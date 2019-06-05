﻿using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using Models.Core;
using Models.Interfaces;

namespace Models
{
    /// <summary>
    /// # [Name]
    /// The module MICROMET, described here, has been developed to allow the calculation of 
    /// potential transpiration for multiple competing canopies that can be either layered or intermingled.
    /// </summary>
    [Serializable]
    [ViewName("UserInterface.Views.GridView")]
    [PresenterName("UserInterface.Presenters.PropertyPresenter")]
    [ValidParent(ParentType = typeof(Simulation))]
    [ValidParent(ParentType = typeof(Zone))]
    public partial class MicroClimate : Model
    {

        #region Constants
        /// <summary>The SVP_ a Teten coefficient</summary>
        private const double svp_A = 6.106;
        /// <summary>The SVP_ b Teten coefficient</summary>
        private const double svp_B = 17.27;
        /// <summary>The SVP_ c Teten coefficient</summary> 
        private const double svp_C = 237.3;
        /// <summary>0 C in Kelvin (k)</summary>
        private const double abs_temp = 273.16;
        /// <summary>universal gas constant (J/mol/K)</summary>
        private const double r_gas = 8.3143;
        /// <summary>molecular weight water (kg/mol)</summary>
        private const double mwh2o = 0.018016;
        /// <summary>molecular weight air (kg/mol)</summary>
        private const double mwair = 0.02897;
        /// <summary>molecular fraction of water to air ()</summary>
        private const double molef = mwh2o / mwair;
        /// <summary>Specific heat of air at constant pressure (J/kg/K)</summary>
        private const double Cp = 1010.0;
        /// <summary>Stefan-Boltzman constant</summary>
        private const double stef_boltz = 5.67E-08;
        /// <summary>constant for cloud effect on longwave radiation</summary>
        private const double c_cloud = 0.1;
        /// <summary>convert degrees to radians</summary>
        private const double Deg2Rad = Math.PI / 180.0;
        /// <summary>Density of water (kg/m3)</summary>
        private const double RhoW = 998.0;
        /// <summary>weights vpd towards vpd at maximum temperature</summary>
        private const double svp_fract = 0.66;
        /// <summary>The sun set angle (degrees)</summary>
        private const double SunSetAngle = 0.0;
        /// <summary>The sun angle for net positive radiation (degrees)</summary>
        private const double SunAngleNetPositiveRadiation = 15;
        /// <summary>Convert hours to seconds</summary>
        private const double hr2s = 60.0 * 60.0;
        /// <summary>von Karman constant</summary>
        private const double vonKarman = 0.41;
        /// <summary>Canopy emissivity</summary>
        private const double CanopyEmissivity = 0.96;
        /// <summary>The soil_emissivity</summary>
        private const double SoilEmissivity = 0.96;

        #endregion


        /// <summary>The clock</summary>
        [Link]
        private Clock Clock = null;

        /// <summary>The weather</summary>
        [Link]
        private IWeather weather = null;

        /// <summary>List of uptakes</summary>
        private List<ZoneMicroClimate> zoneMicroClimates = new List<ZoneMicroClimate>();

        /// <summary>Constructor</summary>
        public MicroClimate()
        {
        }

        /// <summary>This is the length of time within the day during which evaporation will take place</summary>
        private double dayLengthEvap;
        /// <summary>This is the length of time within the day during which the sun is above the horizon</summary>
        private double dayLengthLight;

        /// <summary>Gets or sets the a_interception.</summary>
        [Description("Multiplier on rainfall to calculate interception losses")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Units("mm/mm")]
        public double a_interception { get; set; }

        /// <summary>Gets or sets the b_interception.</summary>
        [Description("Power on rainfall to calculate interception losses")]
        [Bounds(Lower = 0.0, Upper = 5.0)]
        [Units("-")]
        public double b_interception { get; set; }

        /// <summary>Gets or sets the c_interception.</summary>
        [Description("Multiplier on LAI to calculate interception losses")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Units("mm")]
        public double c_interception { get; set; }

        /// <summary>Gets or sets the d_interception.</summary>
        [Description("Constant value to add to calculate interception losses")]
        [Bounds(Lower = 0.0, Upper = 20.0)]
        [Units("mm")]
        public double d_interception { get; set; }

        /// <summary>Gets or sets the soil_albedo.</summary>
        [Description("Soil albedo")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Units("MJ/MJ")]
        public double soil_albedo { get; set; }

        /// <summary>Fraction of solar radiation reaching the soil surface that results in soil heating</summary>
        [Description("Fraction of solar radiation reaching the soil surface that results in soil heating")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Units("MJ/MJ")]
        public double SoilHeatFluxFraction { get; set; }

        /// <summary>The fraction of intercepted rainfall that evaporates at night</summary>
        [Description("The fraction of intercepted rainfall that evaporates at night")]
        [Bounds(Lower = 0.0, Upper = 1.0)]
        [Units("0-1")]
        public double NightInterceptionFraction { get; set; }

        /// <summary>Height of the weather instruments</summary>
        [Description("Height of the weather instruments")]
        [Bounds(Lower = 0.0, Upper = 10.0)]
        [Units("m")]
        public double ReferenceHeight { get; set; }

        /// <summary>Shortwave radiation reaching the surface (ie above the residue layer) (MJ/m2)</summary>
        [Description("Shortwave radiation reaching the surface (ie above the residue layer) (MJ/m2)")]
        [Bounds(Lower = 0.0, Upper = 40.0)]
        [Units("MJ/m2")]
        public double[] SurfaceRS
        {
            get
            {
                double[] values = new double[zoneMicroClimates.Count];
                for (int i = 0; i < zoneMicroClimates.Count; i++)
                    values[i] = zoneMicroClimates[i].SurfaceRs;

                return values;
            }
        }

        /// <summary>Gets the amount of precipitation intercepted by the canopy (mm).</summary>
        [Description("Total intercepted precipitation")]
        [Units("mm")]
        public double PrecipitationInterception
        {
            get { return zoneMicroClimates[0].PrecipitationInterception; }
        }

        /// <summary>Gets the amount of radiation intercepted by the canopy (MJ/m2).</summary>
        [Description("Total intercepted radiation")]
        [Units("MJ/m^2")]
        public double RadiationInterception
        {
            get { return zoneMicroClimates[0].RadiationInterception; }
        }

        /*        /// <summary>Gets the canopy conductance aggregated for all canopy components (mm).</summary>
                [Description("Whole system canopy conductance")]
                [Units("mm")]
                public double CanopyConductance
                {
                    get
                    {
                        double totalConductance = 0.0;
                        for (int i = 0; i <= zoneMicroClimates[0].numLayers - 1; i++)
                            for (int j = 0; j <= zoneMicroClimates[0].Canopies.Count - 1; j++)
                                totalConductance += zoneMicroClimates[0].Canopies[j].Gc[i];
                        return totalConductance;
                    }
                }

                /// <summary>Gets the aerodynamic conductance aggregated for all canopy components (mm).</summary>
                [Description("Whole system aerodynamic conductance")]
                [Units("mm")]
                public double AerodynamicConductance
                {
                    get
                    {
                        double totalConductance = 0.0;
                        for (int i = 0; i <= zoneMicroClimates[0].numLayers - 1; i++)
                            for (int j = 0; j <= zoneMicroClimates[0].Canopies.Count - 1; j++)
                                totalConductance += zoneMicroClimates[0].Canopies[j].Ga[i];
                        return totalConductance;
                    }
                }*/

        /// <summary>Gets the total Penman-Monteith potential evapotranspiration (MJ/m2).</summary>
        [Description("Total Penman-Monteith potential evapotranspiration")]
        [Units("MJ/m^2")]
        public double PetTotal
        {
            get { return PetRadiationTerm + PetAerodynamicTerm; }
        }

        /// <summary>Gets the radiation term of for the Penman-Monteith PET (mm).</summary>
        [Description("Radiation term of for the Penman-Monteith PET")]
        [Units("mm")]
        public double PetRadiationTerm
        {
            get { return zoneMicroClimates[0].petr; }
        }

        /// <summary>Gets the aerodynamic term of for the Penman-Monteith PET (mm).</summary>
        [Description("Aerodynamic term of for the Penman-Monteith PET")]
        [Units("mm")]
        public double PetAerodynamicTerm
        {
            get { return zoneMicroClimates[0].peta; }
        }

        /// <summary>Gets the fraction of the daytime in which the leaves are dry (0-1).</summary>
        [Description("Fraction of the daytime in which the leaves are dry")]
        [Units("-")]
        public double DryLeafTimeFraction
        {
            get { return zoneMicroClimates[0].DryLeafFraction; }
        }

        /// <summary>Gets the total net radiation, long and short waves (MJ/m2).</summary>
        [Description("Net radiation, long and short waves")]
        [Units("MJ/m^2")]
        public double NetRadiation
        {
            get { return NetShortWaveRadiation + NetLongWaveRadiation; }
        }

        /// <summary>Gets the net short wave radiation (MJ/m2).</summary>
        [Description("Net short wave radiation")]
        [Units("MJ/m^2")]
        public double NetShortWaveRadiation
        {
            get { return weather.Radn * (1.0 - zoneMicroClimates[0].Albedo); }
        }

        /// <summary>Gets the net long wave radiation (MJ/m2).</summary>
        [Description("Net long wave radiation")]
        [Units("MJ/m^2")]
        public double NetLongWaveRadiation
        {
            get { return zoneMicroClimates[0].NetLongWaveRadiation; }
        }

        /// <summary>Gets the flux of heat into the soil (MJ/m2).</summary>
        [Description("Soil heat flux")]
        [Units("MJ/m^2")]
        public double SoilHeatFlux
        {
            get { return zoneMicroClimates[0].SoilHeatFlux; }
        }

        /// <summary>Gets the total plant cover (0-1).</summary>
        [Description("Total canopy cover")]
        [Units("-")]
        public double CanopyCover
        {
            get { return MathUtilities.Divide(RadiationInterception, weather.Radn, 0.0); }
        }

        /// <summary>Called when simulation commences.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("Commencing")]
        private void OnSimulationCommencing(object sender, EventArgs e)
        {
            foreach (Zone newZone in Apsim.ChildrenRecursively(this.Parent, typeof(Zone)))
                CreateZoneMicroClimate(newZone);
            if (zoneMicroClimates.Count == 0)
                CreateZoneMicroClimate(this.Parent as Zone);
        }

        /// <summary>
        /// Create a new MicroClimateZone for a given simulation zone
        /// </summary>
        /// <param name="newZone"></param>
        private void CreateZoneMicroClimate(Zone newZone)
        {
            ZoneMicroClimate myZoneMC = new ZoneMicroClimate();
            myZoneMC.zone = newZone;
            myZoneMC.Reset();
            foreach (ICanopy canopy in Apsim.ChildrenRecursively(newZone, typeof(ICanopy)))
                myZoneMC.Canopies.Add(new CanopyType(canopy));
            zoneMicroClimates.Add(myZoneMC);
        }

        /// <summary>Called when the canopy energy balance needs to be calculated.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs"/> instance containing the event data.</param>
        [EventSubscribe("DoEnergyArbitration")]
        private void DoEnergyArbitration(object sender, EventArgs e)
        {
            dayLengthEvap = MathUtilities.DayLength(Clock.Today.DayOfYear, SunAngleNetPositiveRadiation, weather.Latitude);
            dayLengthLight = MathUtilities.DayLength(Clock.Today.DayOfYear, SunSetAngle, weather.Latitude);

            if (zoneMicroClimates.Count == 2 && zoneMicroClimates[0].zone is Zones.RectangularZone && zoneMicroClimates[1].zone is Zones.RectangularZone)
            {
                // We are in a strip crop simulation
                zoneMicroClimates[0].DoCanopyCompartments();
                zoneMicroClimates[1].DoCanopyCompartments();
                CalculateStripCropShortWaveRadiation();

            }
            else // Normal 1D zones are to be used
                foreach (ZoneMicroClimate ZoneMC in zoneMicroClimates)
                {
                    ZoneMC.DoCanopyCompartments();
                    CalculateLayeredShortWaveRadiation(ZoneMC);
                }

            // Light distribution is now complete so calculate remaining micromet equations
            foreach (ZoneMicroClimate ZoneMC in zoneMicroClimates)
            {
                CalculateEnergyTerms(ZoneMC);
                CalculateLongWaveRadiation(ZoneMC);
                CalculateSoilHeatRadiation(ZoneMC);
                CalculateGc(ZoneMC);
                CalculateGa(ZoneMC);
                CalculateInterception(ZoneMC);
                CalculatePM(ZoneMC);
                CalculateOmega(ZoneMC);
                SetCanopyEnergyTerms(ZoneMC);
            }
        }

        ///<summary> Calculate the short wave radiation balance for strip crop system</summary>
        private void CalculateStripCropShortWaveRadiation()
        {
            
            ZoneMicroClimate tallest;
            ZoneMicroClimate shortest;
            if (MathUtilities.Sum(zoneMicroClimates[0].DeltaZ)> MathUtilities.Sum(zoneMicroClimates[1].DeltaZ))
            {
                tallest = zoneMicroClimates[0];
                shortest = zoneMicroClimates[1];
            }
            else
            {
                tallest = zoneMicroClimates[1];
                shortest = zoneMicroClimates[0];
            }

            if (tallest.Canopies.Count>1)
                throw (new Exception("Strip crop light interception model must only have one canopy in zone called "+tallest.zone.Name));
            if (shortest.Canopies.Count > 1)
                throw (new Exception("Strip crop light interception model must only have one canopy in zone called " + shortest.zone.Name));
            //if (tallest.DeltaZ.Length > 1)
            //    throw (new Exception("Strip crop light interception model must only have one canopy layer in zone called " + tallest.zone.Name));
            if (shortest.DeltaZ.Length > 1)
                throw (new Exception("Strip crop light interception model must only have one canopy layer in zone called " + shortest.zone.Name));

            if (MathUtilities.Sum(tallest.DeltaZ) > 0)  // Don't perform calculations if layers are empty
            {
                double Ht = MathUtilities.Sum(tallest.DeltaZ);                // Height of tallest strip
                double Dt = tallest.Canopies[0].Canopy.Depth/1000;            // Depth of tallest strips canopy
                double BHt = Ht - Dt;                                         // Hight of the base of the tallest strips canopy
                double Hs = MathUtilities.Sum(shortest.DeltaZ);               // Height of shortest strip
                if ((Hs > BHt) & (tallest.DeltaZ.Length > 1))
                    throw (new Exception("Shortest canopy must not overlap with talleset canopy if the base height of the tallest canopy is >0"));
                double Wt = (tallest.zone as Zones.RectangularZone).Width;    // Width of tallest strip zone
                double PWt = tallest.Canopies[0].Canopy.Width/1000;           // Width of the canopy in the tallest strip
                double Ws = (shortest.zone as Zones.RectangularZone).Width;   // Width of shortest strip
                double Wo = Math.Min(Math.Max(0, PWt - Wt), Ws);              // Width of the tallest strips canopy that overlaps the shortest strip
                double Ft = (Wt + Wo) / (Wt + Ws);                            // Fraction of space in tallest strips canopy
                double Fs = (Ws - Wo) / (Wt + Ws);                            // Fraction of space in the shortest strips canopy
                double LAIt = MathUtilities.Sum(tallest.LAItotsum);           // LAI of tallest strip
                double LAIs = MathUtilities.Sum(shortest.LAItotsum);          // LAI of shortest strip
                double Kt = tallest.Canopies[0].Ktot;                         // Extinction Coefficient of the tallest strip
                double Ks = shortest.Canopies[0].Ktot;                        // Extinction Coefficient of the shortest strip
                double Httop = Dt;                                            // Height of the top layer in tallest strip (ie distance from top of shortest to top of tallest)
                double LAIttop = LAIt;                                        // LAI of the top layer of the tallest strip (ie LAI in tallest strip above height of shortest strip)
                if (Hs > BHt)
                {
                    Httop = Dt - (Hs - BHt);                                 
                    LAIttop = Httop / Dt * LAIt;
                }
                double LAItbot = LAIt - LAIttop;                              // LAI of the bottom layer of the tallest strip (ie LAI in tallest strip below height of the shortest strip)
                double LAIttophomo = Ft * LAIttop;                            // LAI of top layer of tallest strip if spread homogeneously across all of the space
                double Ftblack = (Math.Sqrt(Math.Pow(Httop, 2) + Math.Pow(Wt, 2)) - Httop) / Wt;  // View factor for top layer of tallest strip
                double Fsblack = (Math.Sqrt(Math.Pow(Httop, 2) + Math.Pow(Ws, 2)) - Httop) / Ws;  // View factor for top layer of shortest strip
                double Tttop = Ft * (Ftblack * Math.Exp(-Kt * LAIttop) 
                                  + Ft * (1 - Ftblack) * Math.Exp(-Kt * LAIttophomo)) 
                          + Fs * Ft * (1 - Fsblack) * Math.Exp(-Kt * LAIttophomo);     //  Transmission of light to bottom of top layer in tallest strip
                double Ttbot = (Tttop * (Math.Exp(-Kt * LAItbot)));                    // Transmission of light to the bottom of the bottom layer in the tallest strip
                double Ts = Fs * (Fsblack +Fs*(1-Fsblack)*Math.Exp(-Kt*LAIttophomo))
                          +Ft*Fs*((1-Ftblack)*Math.Exp(-Kt*LAIttophomo));              //  Transmission of light to bottom of top layer in shortest strip
                double Intttop = 1 - Tttop - Ts;                                       // Fractional interception by the top layer of the tallest strip (ie light intercepted in tallest strip above height of shortest strip)
                double Inttbot = (Tttop * (1 - Math.Exp(-Kt * LAItbot)));              // Fractional interception by the bottom layer of the tallest strip
                double Soilt = Ttbot * Wt/(Wt+Wo);                                     // Fractional transmission to the soil below tallest strip
                double IntsOverlap = Ttbot* Wo/(Wt+Wo) * (1 - Math.Exp(-Ks * LAIs));   //Fractional interception by the shortest strip below the overlap of the tall strip
                double IntsOpen = Ts * (1 - Math.Exp(-Ks * LAIs));                     //Fractional interception by the shortest strip between overlaps 
                double Ints = IntsOverlap + IntsOpen;                                  // Fractional interception by the shortest strip
                double SoilsOverlap = Ttbot * Wo / (Wt + Wo) * (Math.Exp(-Ks * LAIs)); // Fractional interception to the soil below the shortest strip under the overlap
                double SoilsOpen = Ts * (Math.Exp(-Ks * LAIs));                        // Fractional transmission to the soil below the shortest strip in the open
                double Soils = SoilsOverlap + SoilsOpen;                               // Fractional transmission to the soil below shortest strip
                double EnergyBalanceCheck = Intttop + Inttbot + Soilt + Ints + Soils;  // Sum of all light fractions (should equal 1)
                if (Math.Abs(1 - EnergyBalanceCheck) > 0.001)
                    throw (new Exception("Energy Balance not maintained in strip crop light interception model"));

                Ft = (Wt) / (Wt + Ws);  // Remove overlap so scaling back to zone ground area works
                Fs = (Ws) / (Wt + Ws);  // Remove overlap so scaling back to zone ground area works

                tallest.Canopies[0].Rs[0] = weather.Radn * (Intttop + Inttbot) /Ft;  //Fix me.  Currently putting radiation interception into the bottom layer which is not correct when we have a canopy with a base height > 0
                tallest.SurfaceRs = weather.Radn*Soilt/Ft;

                if (shortest.Canopies[0].Rs != null)
                    if (shortest.Canopies[0].Rs.Length>0)
                        shortest.Canopies[0].Rs[0] = weather.Radn * Ints/Fs;
                shortest.SurfaceRs = weather.Radn * Soils/Fs;
            }
            else
            {
                //tallest.Canopies[0].Rs[0] =0;
                tallest.SurfaceRs = weather.Radn;
                //shortest.Canopies[0].Rs[0] = 0;
                shortest.SurfaceRs = weather.Radn;
            }

            
        }

        /// <summary>Calculate the canopy conductance for system compartments</summary>
        private void CalculateGc(ZoneMicroClimate ZoneMC)
        {
            double Rin = weather.Radn;

            for (int i = ZoneMC.numLayers - 1; i >= 0; i += -1)
            {
                double Rflux = Rin * 1000000.0 / (dayLengthEvap * hr2s) * (1.0 - ZoneMC.Albedo);
                double Rint = 0.0;

                for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                {
                    ZoneMC.Canopies[j].Gc[i] = CropCanopyConductance(ZoneMC.Canopies[j].Canopy.Gsmax, ZoneMC.Canopies[j].Canopy.R50, ZoneMC.Canopies[j].Fgreen[i], ZoneMC.layerKtot[i], ZoneMC.LAItotsum[i], Rflux);
                    Rint += ZoneMC.Canopies[j].Rs[i];
                }
                // Calculate Rin for the next layer down
                Rin -= Rint;
            }
        }

        /// <summary>Calculate the aerodynamic conductance for system compartments</summary>
        private void CalculateGa(ZoneMicroClimate ZoneMC)
        {
            double sumDeltaZ = MathUtilities.Sum(ZoneMC.DeltaZ);
            double sumLAI = MathUtilities.Sum(ZoneMC.LAItotsum);
            double totalGa = AerodynamicConductanceFAO(weather.Wind, ReferenceHeight, sumDeltaZ, sumLAI);

            for (int i = 0; i <= ZoneMC.numLayers - 1; i++)
                for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                    ZoneMC.Canopies[j].Ga[i] = totalGa * MathUtilities.Divide(ZoneMC.Canopies[j].Rs[i], ZoneMC.sumRs, 0.0);
        }

        /// <summary>Calculate the interception loss of water from the canopy</summary>
        private void CalculateInterception(ZoneMicroClimate ZoneMC)
        {
            double sumLAI = 0.0;
            double sumLAItot = 0.0;
            for (int i = 0; i <= ZoneMC.numLayers - 1; i++)
                for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                {
                    sumLAI += ZoneMC.Canopies[j].LAI[i];
                    sumLAItot += ZoneMC.Canopies[j].LAItot[i];
                }

            double totalInterception = a_interception * Math.Pow(weather.Rain, b_interception) + c_interception * sumLAItot + d_interception;
            totalInterception = Math.Max(0.0, Math.Min(0.99 * weather.Rain, totalInterception));

            for (int i = 0; i <= ZoneMC.numLayers - 1; i++)
                for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                    ZoneMC.Canopies[j].interception[i] = MathUtilities.Divide(ZoneMC.Canopies[j].LAI[i], sumLAI, 0.0) * totalInterception;
        }

        /// <summary>Calculate the Penman-Monteith water demand</summary>
        private void CalculatePM(ZoneMicroClimate ZoneMC)
        {
            // zero a few things, and sum a few others
            double sumRl = 0.0;
            double sumRsoil = 0.0;
            double sumInterception = 0.0;
            double freeEvapGa = 0.0;

            for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
            {
                sumRl += MathUtilities.Sum(ZoneMC.Canopies[j].Rl);
                sumRsoil += MathUtilities.Sum(ZoneMC.Canopies[j].Rsoil);
                sumInterception += MathUtilities.Sum(ZoneMC.Canopies[j].interception);
                freeEvapGa += MathUtilities.Sum(ZoneMC.Canopies[j].Ga);
            }

            double netRadiation = ((1.0 - ZoneMC.Albedo) * ZoneMC.sumRs + sumRl + sumRsoil) * 1000000.0;   // MJ/J
            netRadiation = Math.Max(0.0, netRadiation);

            double freeEvapGc = freeEvapGa * 1000000.0; // infinite surface conductance
            double freeEvap = CalcPenmanMonteith(netRadiation, weather.MinT, weather.MaxT, weather.VP, weather.AirPressure, dayLengthEvap, freeEvapGa, freeEvapGc);

            ZoneMC.DryLeafFraction = 1.0 - MathUtilities.Divide(sumInterception * (1.0 - NightInterceptionFraction), freeEvap, 0.0);
            ZoneMC.DryLeafFraction = Math.Max(0.0, ZoneMC.DryLeafFraction);

            for (int i = 0; i <= ZoneMC.numLayers - 1; i++)
                for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                {
                    netRadiation = 1000000.0 * ((1.0 - ZoneMC.Albedo) * ZoneMC.Canopies[j].Rs[i] + ZoneMC.Canopies[j].Rl[i] + ZoneMC.Canopies[j].Rsoil[i]);
                    netRadiation = Math.Max(0.0, netRadiation);

                    ZoneMC.Canopies[j].PETr[i] = CalcPETr(netRadiation * ZoneMC.DryLeafFraction, weather.MinT, weather.MaxT, weather.AirPressure, ZoneMC.Canopies[j].Ga[i], ZoneMC.Canopies[j].Gc[i]);
                    ZoneMC.Canopies[j].PETa[i] = CalcPETa(weather.MinT, weather.MaxT, weather.VP, weather.AirPressure, dayLengthEvap * ZoneMC.DryLeafFraction, ZoneMC.Canopies[j].Ga[i], ZoneMC.Canopies[j].Gc[i]);
                    ZoneMC.Canopies[j].PET[i] = ZoneMC.Canopies[j].PETr[i] + ZoneMC.Canopies[j].PETa[i];
                }
        }

        /// <summary>Calculate the aerodynamic decoupling for system compartments</summary>
        private void CalculateOmega(ZoneMicroClimate MCZone)
        {
            for (int i = 0; i <= MCZone.numLayers - 1; i++)
                for (int j = 0; j <= MCZone.Canopies.Count - 1; j++)
                    MCZone.Canopies[j].Omega[i] = CalcOmega(weather.MinT, weather.MaxT, weather.AirPressure, MCZone.Canopies[j].Ga[i], MCZone.Canopies[j].Gc[i]);
        }

        /// <summary>Send an energy balance event</summary>
        private void SetCanopyEnergyTerms(ZoneMicroClimate ZoneMC)
        {
            for (int j = 0; j <= ZoneMC.Canopies.Count - 1; j++)
                if (ZoneMC.Canopies[j].Canopy != null)
                {
                    CanopyEnergyBalanceInterceptionlayerType[] lightProfile = new CanopyEnergyBalanceInterceptionlayerType[ZoneMC.numLayers];
                    double totalPotentialEp = 0;
                    double totalInterception = 0.0;
                    for (int i = 0; i <= ZoneMC.numLayers - 1; i++)
                    {
                        lightProfile[i] = new CanopyEnergyBalanceInterceptionlayerType();
                        lightProfile[i].thickness = ZoneMC.DeltaZ[i];
                        lightProfile[i].amount = ZoneMC.Canopies[j].Rs[i] * ZoneMC.RadnGreenFraction(j);
                        totalPotentialEp += ZoneMC.Canopies[j].PET[i];
                        totalInterception += ZoneMC.Canopies[j].interception[i];
                    }
                    ZoneMC.Canopies[j].Canopy.PotentialEP = totalPotentialEp;
                    ZoneMC.Canopies[j].Canopy.WaterDemand = totalPotentialEp;
                    ZoneMC.Canopies[j].Canopy.LightProfile = lightProfile;
                }
        }
    }
}
