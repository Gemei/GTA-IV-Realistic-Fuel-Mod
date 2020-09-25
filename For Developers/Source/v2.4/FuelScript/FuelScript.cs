/*
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

In jurisdictions that recognize copyright laws, the author or authors
of this software dedicate any and all copyright interest in the
software to the public domain. We make this dedication for the benefit
of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of
relinquishment in perpetuity of all present and future rights to this
software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.
*/

// References
using GTA;
using SlimDX.XInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Text.RegularExpressions;
using AdvancedHookManaged;

// Namespace
namespace FuelScript
{
    // Main Class
    public class FuelScript : Script
    {
        private bool EngineRunning = false;
        /// <summary>
        /// Primary function and the constructor.
        /// RELEASE WARNING, SlimDX.dll should be placed on GTA root folder, NOT the scripts folder.
        /// </summary>
        public FuelScript()
        {
            #if DEBUG
            BindConsoleCommand("setFuel", (GTA.ParameterCollection args) =>
            {
                try
                {
                    CurrentVehicle.Metadata.Fuel = args.ToFloat(0);
                }
                catch (Exception crap) {}
            });
            #endif

            // Set message flags
            ranOutMessageDisplayed = false;
            welcomeMessageDisplayed = false;

            // Get the file version from the assembled DLL.
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(Game.InstallFolder + "\\scripts\\FuelScript.net.dll");

            // Prepend "BETA" if running on debug or compiled as debug.
            string versionPrepend = "";
            #if DEBUG
            versionPrepend = " BETA";
            #endif

            // Set version with prepend if available any.
            string version = fvi.FileVersion + versionPrepend;

            #region External Script Communication Function Binders
            // Script command functions...
            GUID = new Guid("3583e09d-6c44-4820-85e9-93926307d4f8");

            // A different GUID specified by player in config file?
            string extGuid = Settings.GetValueString("GUID", "MISC", "").Trim();
            if (extGuid != "")
            {
                ///
                /// Usage example:
                /// SendScriptCommand(new Guid("3583e09d-6c44-4820-85e9-93926307d4f8"), "SetCurrentFuel", 100.0f);
                /// Still missing documentation on how to use this for what, initially was intended to implement the car saving feature externally.
                ///
                BindScriptCommand("GetCurrentFuel", new ScriptCommandDelegate(SendCurrentFuel));
                BindScriptCommand("GetCurrentFuelPercentage", new ScriptCommandDelegate(SendCurrentFuelPercentage));
                BindScriptCommand("GetCurrentDrain", new ScriptCommandDelegate(SendCurrentDrain));
                BindScriptCommand("GetCurrentFuelBottles", new ScriptCommandDelegate(SendCurrentFuelBottles));
                BindScriptCommand("SetCurrentFuel", new ScriptCommandDelegate(SetCurrentFuel));
                BindScriptCommand("SetCurrentFuelPercentage", new ScriptCommandDelegate(SetCurrentFuelPercentage));
                BindScriptCommand("SetVehicleFuel", new ScriptCommandDelegate(SetVehicleFuel));
                BindScriptCommand("SetVehicleFuelPercentage", new ScriptCommandDelegate(SetVehicleFuelPercentage));
                ExtScriptGUID = new Guid(extGuid);
            }
            // No GUID?
            else
            {
                ExtScriptGUID = Guid.Empty;
            }
            #endregion

            // Hook the ticking function.
            this.Interval = 1000;
            this.Tick += new EventHandler(FuelScript_Tick);

            // Start a new log session by putting this dashed line so we can easily identify it
            Log("FuelScript", "NEW GAME SESSION STARTED ON " + System.DateTime.Now + " - GTA IV");

            // Then log the rest of bla blas...
            Log("FuelScript", "Realistic Fuel Mod " + version + " for GTA IV loaded under GTA IV " + Game.Version.ToString() + " successfully.");
            Log("FuelScript", "Realistic Fuel Mod is an open-source software on public domain license (https://code.google.com/p/realistic-fuel-mod).");
            Log("FuelScript", "Based on Ultimate Fuel Script v2.1 (https://code.google.com/p/ultimate-fuel-script).");
            Log("FuelScript", "Realistic Fuel Mod " + version + " found dsound.dll " + ((File.Exists(Game.InstallFolder + "\\dsound.dll")) ? "present" : "not present") + ", xlive.dll " + ((File.Exists(Game.InstallFolder + "\\xlive.dll")) ? "present" : "not present") + " and SlimDX.dll " + ((File.Exists(Game.InstallFolder + "\\SlimDX.dll")) ? "present." : "not present."));

            Log("FuelScript", "Loading settings file: FuelScript.ini...");

            // Load the settings file.
            SettingsFile.Open("FuelScript.ini");
            Settings.Load();

            // If it made this long, the settings file must be loaded without an error.
            Log("FuelScript", "Settings file: FuelScripts.ini is loaded.");

            // Set as not refueling when the script is starting.
            Refueling = false;

            // Log as reading the settings file.
            Log("FuelScript", "Reading settings file: FuelScripts.ini...");

            // Set max fuel bottles from settings.
            MaxFuelBottles = Settings.GetValueInteger("MAXFUELBOTTLES", "MISC", 5);

            UsedFuelBottles = (Settings.GetValueInteger("FREEBOTTLES", "MISC", 3) > MaxFuelBottles) ? 0 : MaxFuelBottles - Settings.GetValueInteger("FREEBOTTLES", "MISC", 3);

            // Set fuel bottle cost from settings.
            FuelBottleCost = Settings.GetValueFloat("FUELBOTTLECOST", "MISC", 129.99f);

            // Set emergency fuel service cost from settings.
            ServiceCost = Settings.GetValueFloat("SERVICECOST", "MISC", 899.99f);

            // Show the script status.
            if (Settings.GetValueBool("STARTUPTEXT", "TEXTS", true))
            {

                AGame.PrintText(String.Format("Realistic Fuel Mod {0} loaded correctly.", version));

                ShowMessage(String.Format("You have {0} free emergency fuel bottles.", MaxFuelBottles - UsedFuelBottles), 10000);
            }

            // Defualt display type is: CLASSIC.
            switch (Settings.GetValueString("MODE", "DASHBOARD", "CLASSIC").ToUpper().Trim())
            {
                // Developer mode display panel.
                case "DEV":
                    this.PerFrameDrawing += new GraphicsEventHandler(FuelScript_PerFrameDrawing_devMode);
                    // Bind Q to reload settings file.
                    this.BindKey(Keys.Q, Settings.Load);
                    break;

                // Digital mode display panel (with digits).
                case "DIGITAL":
                    this.PerFrameDrawing += new GraphicsEventHandler(FuelScript_PerFrameDrawing_digitalMode);
                    break;

                // Classical mode display panel (with meters and several digits).
                // Recommended mode for the public releases.
                case "CLASSIC":
                    this.PerFrameDrawing += new GraphicsEventHandler(FuelScript_PerFrameDrawing_classicMode);
                    break;
            }

            // Log which display panel mode was chosen by the player.
            Log("FuelScript", "Fuel display panel has been selected as: " + Settings.GetValueString("MODE", "DASHBOARD", "CLASSIC").ToUpper().Trim() + " display mode.");

            // Bind the keydown function.
            this.KeyDown += new GTA.KeyEventHandler(FuelScript_KeyDown);

            // Bind the keyup function.
            this.KeyUp += new GTA.KeyEventHandler(FuelScript_KeyUp);

            // Fuel meter dashboard location, X, Y and W (width).
            Dashboard = new PointF(Settings.GetValueFloat("X", "DASHBOARD", 0.0f), Settings.GetValueFloat("Y", "DASHBOARD", 0.0f));
            GaugeWidth = Settings.GetValueFloat("W", "DASHBOARD", 0.11f);

            // Speed multipier if needed.
            SpeedMultiplier = (Settings.GetValueString("SPEED", "MISC", "KPH").ToUpper().Trim() == "KPH") ? 3.6f : 2.23693629f;

            // Log key mappings for diagnostics (not really necessary, whatever).
            Log("FuelScript", "Settings: Refuel Key - " + Settings.GetValueKey("REFUELKEY", "KEYS", Keys.E) + ", Bottle Use Key - " + Settings.GetValueKey("BOTTLEUSEKEY", "KEYS", Keys.U) + ", Bottle Buy Key - " + Settings.GetValueKey("BOTTLEBUYKEY", "KEYS", Keys.B) + ", Service Key - " + Settings.GetValueKey("SERVICEKEY", "KEYS", Keys.K) + ".");

            // Select the input method.
            // Is he chosen to use a GamePad? If so SlimDX.dll exists?
            if (Settings.GetValueBool("GAMEPAD", "MISC", false) && File.Exists(Game.InstallFolder + "\\SlimDX.dll"))
            {
                // OK, here you go with the GamePad.
                GamePad = new Controller(UserIndex.One);
                Log("FuelScript", "Selected active controller type as: GAMEPAD.");
            }
            // Oops, either player chosen to use Keyboard or he don't have SlimDX.dll in GTA IV directory, which is required.
            else
            {
                // Go with the keyboard, it always better, right?
                GamePad = null;
                Log("FuelScript", "Selected active controller type as: KEYBOARD.");
            }

            // No... no... no it's not in reserve.
            this.OnReserve = false;

            // Bind phone number GET-555-FUEL (438-555-3835) to Emergency Fuel Services.
            BindPhoneNumber("4385553835", new PhoneDialDelegate(PhoneNumberHandler));

            // Classic mode meter digits styles...
            // FuelMeterFont.Color = ColorIndex.SecuricorLightGray;
            FuelMeterFont.Effect = FontEffect.Edge;
            FuelMeterFont.EffectSize = 1;
            FuelMeterFont.EffectColor = ColorIndex.Black;

            // Set the beta watermark PNG file.
            #if DEBUG
            BetaMark = Resources.GetTexture("beta_mark.png");
            #endif

            /// <summary>
            /// Loads all the stations.
            /// There are 3 types of stations: STATION, HELISTATION, BOATSTATION. 
            /// Each type can have up to 254 stations.
            /// The first identifier is 1, the last is 255.
            /// The identifiers must be consecutive (1, 2, 3, 4. NOT 1, 3, 5)
            /// </summary>
            #region Load Dueling Stations
            try
            {
                // Log as placing blips...
                Log("FuelScript", "Placing fueling station blips on the map...");

                // Values for stations counters purely for logs.
                int StationsCount = 0;
                int CarStationsCount = 0;
                int HeliStationsCount = 0;
                int BoatStationsCount = 0;

                // Fuel stations for cars and bikes are enabled?
                if (Settings.GetValueBool("CARS", "MISC", true))
                {
                    // Load stations...
                    for (byte i = 1; i <= Byte.MaxValue; i++)
                    {
                        // Get the station's location.
                        Vector3 StationLocation = Settings.GetValueVector3("LOCATION", "STATION" + i,
                            new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                        if (StationLocation.X == -123456789.0987654321f && StationLocation.Y == -123456789.0987654321f && StationLocation.Z == -123456789.0987654321f)
                            break;
                        // OK to proceed...
                        else
                        {
                            // Add a blip...
                            Blip StationBlip = GTA.Blip.AddBlip(StationLocation);
                            // Choose the icon...
                            StationBlip.Icon = (BlipIcon)79;
                            // Set a name...
                            StationBlip.Name = (Settings.GetValueString("NAME", "STATION" + i, "Fuel Station").ToUpper().Trim().Length > 30)
                                ? Settings.GetValueString("NAME", "STATION" + i, "Fuel Station").ToUpper().Trim().Substring(0, 29)
                                : Settings.GetValueString("NAME", "STATION" + i, "Fuel Station").ToUpper().Trim();
                            // Check if the blip should be easily visible
                            StationBlip.Display = Settings.GetValueBool("DISPLAY", "STATION" + i, true) ? BlipDisplay.MapOnly : BlipDisplay.Hidden;
                            // It's ours...
                            StationBlip.Friendly = true;
                            // Auto set route?
                            StationBlip.RouteActive = false;
                            // Minimap only.
                            StationBlip.ShowOnlyWhenNear = true;
                        }

                        // Stations increment.
                        StationsCount = StationsCount + 1;

                        // Car stations increment.
                        CarStationsCount = CarStationsCount + 1;
                    }
                }

                // Fueling stations for Helicopters are enabled?
                if (Settings.GetValueBool("HELIS", "MISC", true))
                {
                    // Load stations...
                    for (byte i = 1; i <= Byte.MaxValue; i++)
                    {
                        // Get the station's location.
                        Vector3 StationLocation = Settings.GetValueVector3("LOCATION", "HELISTATION" + i,
                            new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                        if (StationLocation.X == -123456789.0987654321f && StationLocation.Y == -123456789.0987654321f && StationLocation.Z == -123456789.0987654321f)
                            break;
                        // OK to proceed...
                        else
                        {
                            // Add a blip...
                            Blip StationBlip = GTA.Blip.AddBlip(StationLocation);
                            // Choose the icon...
                            StationBlip.Icon = (BlipIcon)56;
                            // Set a name...
                            StationBlip.Name = (Settings.GetValueString("NAME", "HELISTATION" + i, "Fuel Station").ToUpper().Trim().Length > 30)
                                ? Settings.GetValueString("NAME", "HELISTATION" + i, "Fuel Station").ToUpper().Trim().Substring(0, 29)
                                : Settings.GetValueString("NAME", "HELISTATION" + i, "Fuel Station").ToUpper().Trim();
                            // Check if the blip should be easily visible
                            StationBlip.Display = Settings.GetValueBool("DISPLAY", "HELISTATION" + i, true) ? BlipDisplay.MapOnly : BlipDisplay.Hidden;
                            // It's ours...
                            StationBlip.Friendly = true;
                            // Auto set route?
                            StationBlip.RouteActive = false;
                            // Minimap only...
                            StationBlip.ShowOnlyWhenNear = true;
                        }

                        // Stations increment.
                        StationsCount = StationsCount + 1;

                        // Helicopter stations increment.
                        HeliStationsCount = HeliStationsCount + 1;
                    }
                }

                // Fueling stations for boats are enabled?
                if (Settings.GetValueBool("BOATS", "MISC", true))
                {
                    // Load stations...
                    for (byte i = 1; i <= Byte.MaxValue; i++)
                    {
                        // Get the station's location.
                        Vector3 StationLocation = Settings.GetValueVector3("LOCATION", "BOATSTATION" + i,
                            new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                        if (StationLocation.X == -123456789.0987654321f && StationLocation.Y == -123456789.0987654321f && StationLocation.Z == -123456789.0987654321f)
                            break;
                        // OK to proceed...
                        else
                        {
                            // Add a blip...
                            Blip StationBlip = GTA.Blip.AddBlip(StationLocation);
                            // Choose an icon...
                            StationBlip.Icon = (BlipIcon)48;
                            // Set a name...
                            StationBlip.Name = (Settings.GetValueString("NAME", "BOATSTATION" + i, "Fuel Station").ToUpper().Trim().Length > 30)
                                ? Settings.GetValueString("NAME", "BOATSTATION" + i, "Fuel Station").ToUpper().Trim().Substring(0, 29)
                                : Settings.GetValueString("NAME", "BOATSTATION" + i, "Fuel Station").ToUpper().Trim();
                            // Check if the blip should be easily visible
                            StationBlip.Display = Settings.GetValueBool("DISPLAY", "BOATSTATION" + i, true) ? BlipDisplay.MapOnly : BlipDisplay.Hidden;
                            // It's ours...
                            StationBlip.Friendly = true;
                            // Auto set route?
                            StationBlip.RouteActive = false;
                            // Minimap only...
                            StationBlip.ShowOnlyWhenNear = true;
                        }

                        // Stations increment.
                        StationsCount = StationsCount + 1;

                        // Boat stations increment.
                        BoatStationsCount = BoatStationsCount + 1;
                    }
                }

                // Log how much fuel stations has been found...
                Log("FuelScript", "Finished placing blips: " + StationsCount + " blips placed. " + CarStationsCount + " car, " + HeliStationsCount + " helicopter and " + BoatStationsCount + " boat stations.");

                // The outro? Perhaps?
                // Game.DisplayText("Based on the source of Ultimate Fuel Script v2.1", 3000);
            }
            catch (Exception crap) { Log("ERROR: FuelScript", crap.Message); }
            #endregion
        }

        #region Variables and Properties
        /// <summary>
        /// True while fuel level is 0 and message was displayed.
        /// </summary>
        private bool ranOutMessageDisplayed;
        /// <summary>
        /// True while player is at a fuel station and the message 'Welcome to ' or 'You can steal fuel from ' has beeing displayed
        /// </summary>
        private bool welcomeMessageDisplayed;
        /// <summary>
        /// Where to send data
        /// </summary>
        Guid ExtScriptGUID;
        /// <summary>
        /// Fuel meter styled font
        /// </summary>
        private GTA.Font FuelMeterFont = new GTA.Font(0.030f, FontScaling.ScreenUnits);
        /// <summary>
        /// Determines if the CurrentVehicle as already entered the reserve level.
        /// </summary>
        private bool OnReserve;
        /// <summary>
        /// Used for classic mode only.
        /// </summary>
        private float GaugeWidth;
        /// <summary>
        /// To keep track of the Flashinging sequence in reserve levels, this can probably be changed to a lower allocation later
        /// </summary>
        private int Flashing = 0;
        /// <summary>
        /// mps to knots
        /// </summary>
        private const float Knots = 1.94384449f;
        /// <summary>
        /// Determines if speed is shown in KPH or MPH
        /// </summary>
        private float SpeedMultiplier;
        /// <summary>
        /// The location of the dash board
        /// </summary>
        private PointF Dashboard;
        /// <summary>
        /// Holds the last vehicle the player has driven
        /// </summary>
        private Vehicle LastVehicle;
        /// <summary>
        /// Returns true if the player is refueling.
        /// </summary>
        private bool Refueling;
        /// <summary>
        /// Used to debt to the total money from the player's money value.
        /// </summary>
        private float RefuelAmount;
        /// <summary>
        /// Only used in devMode
        /// </summary>
        private float DrainSpeed;
        /// <summary>
        /// Current game, if aplicable.
        /// </summary>
        private Controller GamePad;
        /// <summary>
        /// Station names at the config file
        /// </summary>
        private string StationName;
        /// <summary>
        /// Keeps track of fuel bottles used
        /// </summary>
        private int UsedFuelBottles;
        /// <summary>
        /// How much times player can use emergency reserved fuel bottles
        /// </summary>
        private int MaxFuelBottles;
        /// <summary>
        /// How much one fuel bottle cost to refil it
        /// </summary>
        private float FuelBottleCost;
        /// <summary>
        /// Emergency fuel service vehicle
        /// </summary>
        private Vehicle ServiceVehicle;
        /// <summary>
        /// Emergency fuel service ped
        /// </summary>
        private Ped ServicePed;
        /// <summary>
        /// Emergency fuel service cost
        /// </summary>
        private float ServiceCost;
        /// <summary>
        /// Beta Watermark Texture (embedded)
        /// </summary>
        GTA.Texture BetaMark;
        /// <summary>
        /// Alias for Player.Character.CurrentVehicle
        /// </summary>
        private Vehicle CurrentVehicle
        { get { return (Player.Character.isInVehicle()) ? Player.Character.CurrentVehicle : null; } }
        #endregion

        #region External Script Communication Functions
        /// <summary>
        /// Send current vehicle's fuel value, use command 'GetCurrentFuel'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SendCurrentFuel(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                if (ExtScriptGUID != Guid.Empty && Player.Character.isInVehicle())
                    SendScriptCommand(ExtScriptGUID, "CurrentFuel", CurrentVehicle.Metadata.Fuel);
            }
            catch (Exception crap) { Log("ERROR: SendCurrentFuel", crap.Message); }
        }
        /// <summary>
        /// Send current vehicle's fuel value as percentage of maximum capacity, use command 'GetCurrentFuelPercentage'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SendCurrentFuelPercentage(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                float FuelPercentage = (Convert.ToInt32(CurrentVehicle.Metadata.Fuel) * 100) / Convert.ToInt32(CurrentVehicle.Metadata.MaxTank);
                if (ExtScriptGUID != Guid.Empty && Player.Character.isInVehicle())

                    SendScriptCommand(ExtScriptGUID, "CurrentFuelPercentage", String.Format("{0:00}", FuelPercentage));
            }
            catch (Exception crap) { Log("ERROR: SendCurrentFuelPercentage", crap.Message); }
        }
        /// <summary>
        /// Send current vehicle's fuel drain, use command 'SendCurrentDrain'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SendCurrentDrain(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                if (ExtScriptGUID != Guid.Empty && Player.Character.isInVehicle())
                    SendScriptCommand(ExtScriptGUID, "CurrentDrain", this.DrainSpeed);
            }
            catch (Exception crap) { Log("ERROR: SendCurrentDrain", crap.Message); }
        }
        /// <summary>
        /// Send current vehicle's fuel drain, use command 'GetCurrentFuelBottles'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SendCurrentFuelBottles(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                if (ExtScriptGUID != Guid.Empty && Player.Character.isInVehicle())
                    SendScriptCommand(ExtScriptGUID, "CurrentFuelBottles", (this.MaxFuelBottles - this.UsedFuelBottles));
            }
            catch (Exception crap) { Log("ERROR: SendCurrentFuelBottles", crap.Message); }
        }
        /// <summary>
        /// Sets the current fuel level of the current car with the value specified by Parameter[0], this value must be parseable to float.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SetCurrentFuel(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                if (Player.Character.isInVehicle())
                {
                    float newFuelValue = Parameter.Convert<float>(0);
                    CurrentVehicle.Metadata.Fuel = newFuelValue;
                }
            }
            catch (Exception crap) { Log("ERROR: SendCurrentFuel", crap.Message); }
        }
        /// <summary>
        /// Sets the current fuel level of the current car with the percentage value specified by Parameter[0], this value must be parseable to float.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SetCurrentFuelPercentage(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                if (Player.Character.isInVehicle())
                {
                    float FuelPercentage = Parameter.Convert<float>(0);
                    float newFuelValue = (FuelPercentage * CurrentVehicle.Metadata.MaxTank) / 100;
                    CurrentVehicle.Metadata.Fuel = newFuelValue;
                }
            }
            catch (Exception crap) { Log("ERROR: SetCurrentFuelPercentage", crap.Message); }
        }
        /// <summary>
        /// Sets the current fuel level of the car specified by Parameter[1] with the value specified by Parameter[0], this value must be parseable to float.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SetVehicleFuel(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                Vehicle v = Parameter.Convert<Vehicle>(1);
                float newFuelValue = Parameter.Convert<float>(0);
                v.Metadata.Fuel = newFuelValue;
            }
            catch (Exception crap) { Log("ERROR: SetVehicleFuel", crap.Message); }
        }
        /// <summary>
        /// Sets the current fuel level of the car specified by Parameter[1] with the percentage value specified by Parameter[0], this value must be parseable to float.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="Parameter"></param>
        private void SetVehicleFuelPercentage(GTA.Script sender, GTA.ObjectCollection Parameter)
        {
            try
            {
                Vehicle v = Parameter.Convert<Vehicle>(1);
                float FuelPercentage = Parameter.Convert<float>(0);
                float newFuelValue = (FuelPercentage * CurrentVehicle.Metadata.MaxTank) / 100;
                v.Metadata.Fuel = newFuelValue;
            }
            catch (Exception crap) { Log("ERROR: SetVehicleFuelPercentage", crap.Message); }
        }
        #endregion

        #region Methods and Functions
        /// <summary>
        /// Saves an exception's message with the current date and time, and the method that originated it.
        /// </summary>
        /// <param name="methodName">The method that originated it</param>
        /// <param name="message">The exception's message</param>
        private void Log(string methodName, string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(message) && Settings.GetValueBool("LOG", "MISC", true))
                {
                    using (StreamWriter streamWriter = File.AppendText(Application.StartupPath + "\\scripts\\FuelScript.log"))
                    {
                        streamWriter.WriteLine(DateTime.Now.ToString("hh:mm:ss.fff tt") + ": " + methodName + "() - " + message);
                        streamWriter.Close();
                    }
                }
            }
            catch { }

            // Show this to developer that a log is available for review.
            finally
            {
                if (Settings.GetValueString("MODE", "DASHBOARD", "CLASSIC").ToUpper().Trim() == "DEV")
                {
                    Game.DisplayText("Check log - " + message, 3000);
                }
            }

        }
        /// <summary>
        /// Show message with modern formatted text.
        /// </summary>
        /// <param name="message">Message you want to show</param>
        /// <param name="time">How much it should be on screen</param>
        internal void ShowMessage(string message, int time)
        {
            // Call out for the native function.
            GTA.Native.Function.Call("PRINT_STRING_WITH_LITERAL_STRING_NOW", "STRING", message, time, true);
        }
        /// <summary>
        /// Handles the phone number dialled.
        /// </summary>
        private void PhoneNumberHandler()
        {
            try
            {
                // Make sure player is in a vehicle and driving seat.
                // And make sure it's a ground vehicle!
                if (Player.Character.isInVehicle() && Player == CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) && (CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike))
                {
                    // Player ran out of all options?
                    if (CurrentVehicle.Metadata.Fuel == 0 && UsedFuelBottles == MaxFuelBottles)
                    {
                        // Does the player have enough money to call emergency fuel service?
                        if (Player.Money > Convert.ToInt32(ServiceCost))
                        {
                            // Show while initializing...
                            if (Settings.GetValueBool("EMERGENCYCALLTEXT", "TEXTS", true))
                            {
                                AGame.PrintText("Calling Emergency Fuel Service...");
                            }

                            // Lock the doors to avoid from player getting out.
                            CurrentVehicle.DoorLock = DoorLock.ImpossibleToOpen;

                            // Fuel Service Drive To position.
                            Vector3 DriveToPosition = CurrentVehicle.GetOffsetPosition(new Vector3(5.0f, 0.0f, 0.0f));

                            // Hood position.
                            Vector3 HoodPosition = CurrentVehicle.GetOffsetPosition(new Vector3(0.0f, 3.0f, 0.0f));

                            // Create a fuel bowser.
                            ServiceVehicle = World.CreateVehicle(new Model("packer"), World.GetNextPositionOnStreet(Player.Character.Position.Around(100.0f)));

                            // Wait until it creates.
                            while (!ServiceVehicle.Exists())
                            {
                                Wait(500);
                            }

                            // Only fuel tank required, hide other extra parts.
                            ServiceVehicle.Extras(1).Enabled = true;
                            ServiceVehicle.Extras(3).Enabled = false;
                            ServiceVehicle.Extras(5).Enabled = false;

                            // Make proof to all dangers.
                            ServiceVehicle.MakeProofTo(true, true, true, true, true);

                            // Place on the street properly.
                            ServiceVehicle.PlaceOnNextStreetProperly();

                            // Create a mechanic ped.
                            ServicePed = ServiceVehicle.CreatePedOnSeat(VehicleSeat.Driver, new Model("m_y_mechanic_02"));

                            // Wait until ped creates.
                            while (!ServicePed.Exists())
                            {
                                Wait(500);
                            }

                            // Add a blip to the ped.
                            Blip ServiceBlip = ServicePed.AttachBlip();

                            // Use the fuel station icon.
                            ServiceBlip.Icon = BlipIcon.Building_Garage;

                            // But make it green, so it's identically different.
                            ServiceBlip.Color = BlipColor.Green;

                            // Show on map only?
                            // ServiceBlip.Display = BlipDisplay.MapOnly;

                            // Show only when the blip is near our position?
                            ServiceBlip.ShowOnlyWhenNear = true;

                            // A name to the blip which visible on the map when player mouse hover the blip.
                            ServiceBlip.Name = "Fuel Service Agent";

                            // Make him a god so he won't die in a natural disaster before he reaches us.
                            ServicePed.Invincible = true;

                            // Block his permenent events.
                            ServicePed.BlockPermanentEvents = true;

                            // Respect the player.
                            ServicePed.ChangeRelationship(RelationshipGroup.Player, Relationship.Respect);

                            // Recruite him?
                            // ServicePed.BecomeMissionCharacter();

                            // Clear all previous pending tasks.
                            ServicePed.Task.ClearAll();

                            // Keep focused to the new tasks.
                            ServicePed.Task.AlwaysKeepTask = true;

                            // Stay on new tasks until we clear them again.
                            ServicePed.Task.Wait(-1);

                            // Load all paths nodes so the ped can find paths easily.
                            Game.LoadAllPathNodes = true;

                            // If ped is not in the vehicle, get him inside it.
                            if (!ServicePed.isInVehicle())
                            {
                                ServicePed.Task.EnterVehicle(ServiceVehicle, VehicleSeat.Driver);
                            }

                            // Wait and check whether the ped is in vehicle or not.
                            while (!ServicePed.isInVehicle())
                            {
                                Wait(500);
                            }

                            // Drive to the scene.
                            ServicePed.Task.DriveTo(DriveToPosition, 15.0f, false, true);

                            // Show after creating required objects.
                            if (Settings.GetValueBool("EMERGENCYONWAYTEXT", "TEXTS", true))
                            {
                                AGame.PrintTextForever("An agent is on it's way to your scene... Hold [T] to track him in the radar.");
                            }

                            Log("PhoneNumberHandler", "Player called to the emergency fuel services.");

                            // Wait until he gets near with his vehicle.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // That's enough, get him out of vehicle.
                            ServicePed.Task.LeaveVehicle(ServiceVehicle, true);

                            // Wait until he done exiting the vehicle.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // Run to the hood of the target vehicle.
                            ServicePed.Task.RunTo(HoodPosition, false);

                            // Wait until he reaches there.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // Show when the service agent is near the player.
                            if (Settings.GetValueBool("EMERGENCYAGENTTEXT", "TEXTS", true))
                            {
                                AGame.PrintText("The agent is here, he will refuel and repair your vehicle.");
                            }

                            // Turn to our vehicle's side.
                            ServicePed.Task.TurnTo(CurrentVehicle.Position);

                            // Wait until he done turning.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // Open the hood.
                            ServicePed.Task.PlayAnimation(new AnimationSet("amb@bridgecops"), "open_boot", 4.0f);

                            // Open it... really...
                            CurrentVehicle.Door(VehicleDoor.Hood).Open();
                            Wait(1200);

                            // Do his magic...
                            ServicePed.Task.PlayAnimation(new AnimationSet("misstaxidepot"), "workunderbonnet", 4.0f);

                            // Wait until Niko done repairing the vehicle.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // Close the hood.
                            ServicePed.Task.PlayAnimation(new AnimationSet("amb@bridgecops"), "close_boot", 4.0f);
                            Wait(500);

                            // Close it... really...
                            CurrentVehicle.Door(VehicleDoor.Hood).Close();
                            Wait(1000);

                            // Deduct the cost.
                            Player.Money -= Convert.ToInt32(ServiceCost);
                            // Display the balance.
                            GTA.Native.Function.Call("DISPLAY_CASH", true);

                            // Then give him full tank fuel!
                            CurrentVehicle.Metadata.Fuel = CurrentVehicle.Metadata.MaxTank;
                            // And bonus! 5 Fuel bottles!
                            UsedFuelBottles = 0;
                            // Not on reserve.
                            OnReserve = false;

                            // Start it up.
                            CurrentVehicle.EngineRunning = true;
                            EngineRunning = true;
                            // Turn off hazard lights.
                            CurrentVehicle.HazardLightsOn = false;
                            // Repair the engine.
                            CurrentVehicle.EngineHealth = 1000.0f;

                            // Let the player know.
                            if (Settings.GetValueBool("EMERGENCYDONETEXT", "TEXTS", true))
                            {
                                AGame.PrintText(String.Format("Paid ${0}. Thanks for calling the emergency fuel service.", ServiceCost));
                            }

                            Log("PhoneNumberHandler", "Player got " + Convert.ToInt32(CurrentVehicle.Metadata.Fuel) + " litre(s) of fuel and " + MaxFuelBottles + " fuel bottles billed $" + ServiceCost + ".");

                            // Unlock the doors.
                            CurrentVehicle.DoorLock = DoorLock.None;

                            // Block permenent events for the final tasks.
                            ServicePed.BlockPermanentEvents = true;

                            // Clear previous tasks.
                            ServicePed.Task.ClearAll();

                            // Focus on final tasks.
                            ServicePed.Task.AlwaysKeepTask = true;

                            // Wait until Niko done doing previous tasks given by script.
                            while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", ServicePed))
                            {
                                Wait(100);
                            }

                            // Get back on his vehicle.
                            ServicePed.Task.EnterVehicle(ServiceVehicle, VehicleSeat.Driver);

                            // Delete the blip.
                            ServiceBlip.Delete();

                            // Restore vehicle states.
                            ServiceVehicle.MakeProofTo(false, false, false, false, false);

                            // Run all over the city as you wish!
                            ServicePed.Task.CruiseWithVehicle(ServiceVehicle, 35.0f, true);

                            // We don't need him or his vehicle?
                            // Really? Who does want a bowser? :D
                            ServicePed.NoLongerNeeded();
                            ServiceVehicle.NoLongerNeeded();
                        }
                        else
                        {
                            // Let the player know.
                            AGame.PrintText("You don't have enough money to request this service");
                            Log("PhoneNumberHandler", "Player did not have enough money to request emergency fuel service.");
                        }
                    }
                    // If player still have a way to refuel on mobile.
                    else
                    {
                        // Let him know.
                        AGame.PrintText("You don't need this service yet.");
                    }
                }
            }
            catch (Exception crap) { Log("ERROR: PhoneNumberHandler", crap.Message); }
        }
        /// <summary>
        /// Use ONLY when player is in vehicle!
        /// Retrieves the values of DRAIN, MAXTANK and RESERVE, from the loaded ini file. First looks for the car hash, then for the car name, then defaults.
        /// Generates a random amount of fuel, if an amount can't be find under CurrentVehicle.Metadata.Fuel.
        /// Calculates the amount of fuel to be drain and drains it, and prevents the car engine from running when out of fuel.
        /// </summary>
        private void DrainFuel()
        {
            try
            {
                // Set station name right now!
                if (Player.Character.isInVehicle() && Player == CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver))
                {
                    StationName = (CurrentVehicle.Model.isBoat) ? "BOATSTATION" : (CurrentVehicle.Model.isHelicopter) ? "HELISTATION" : "STATION";
                    EngineRunning = true;
                }
                else 
                {
                    EngineRunning = false;
                }

                #region Vehicle Fuel Level Checker
                try { float f = CurrentVehicle.Metadata.Fuel; }
                catch
                {
                    // If it does not exists...
                    // First the ini file is checked for the vehicle's hash code, then for the vehicle's name.
                    if (CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike)
                    {
                        // Get max tank capacity
                        CurrentVehicle.Metadata.MaxTank = Settings.GetValueInteger("TANK", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("TANK", CurrentVehicle.Name, 100));

                        // Get draining speed
                        CurrentVehicle.Metadata.Drain = Settings.GetValueInteger("DRAIN", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("DRAIN", CurrentVehicle.Name, 10));

                        // Get reserved fuel capacity
                        CurrentVehicle.Metadata.Reserve = Settings.GetValueInteger("RESERVE", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("RESERVE", CurrentVehicle.Name, 10));

                        // Random it vehicle to vehicle
                        CurrentVehicle.Metadata.Fuel = (int)new Random().Next(CurrentVehicle.Metadata.Reserve + 1, CurrentVehicle.Metadata.MaxTank);
                    }
                    else if (CurrentVehicle.Model.isHelicopter || CurrentVehicle.Model.isBoat)
                    {
                        // Get max tank capacity
                        CurrentVehicle.Metadata.MaxTank = Settings.GetValueInteger("TANK", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("TANK", CurrentVehicle.Name, 200));

                        // Get draining speed
                        CurrentVehicle.Metadata.Drain = Settings.GetValueInteger("DRAIN", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("DRAIN", CurrentVehicle.Name, 20));

                        // Get reserved fuel capacity
                        CurrentVehicle.Metadata.Reserve = Settings.GetValueInteger("RESERVE", CurrentVehicle.GetHashCode().ToString(),
                        Settings.GetValueInteger("RESERVE", CurrentVehicle.Name, 20));

                        // Random it vehicle to vehicle
                        CurrentVehicle.Metadata.Fuel = (int)new Random().Next(CurrentVehicle.Metadata.Reserve + 1, CurrentVehicle.Metadata.MaxTank);
                    }

                }
                #endregion

                #region Fuel Draining and Vehicle Fuel Status
                // If Niko have fuel in the vehicle.
                if (CurrentVehicle.Metadata.Fuel > 0.0f)
                {
                    // keep reseting help message
                    ranOutMessageDisplayed = false;
                    // Keep hazard lights turned off.
                    // Causing issue with IVIndicator mod.
                    //CurrentVehicle.HazardLightsOn = false;

                    // Draining enabled for cars and bikes?
                    if ((CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike) && EngineRunning && Settings.GetValueBool("CARS", "MISC", true))
                    {
                        // CurrentVehicle.Metadata.Drain is a user defined constant, defaults to 20
                        DrainSpeed = CurrentVehicle.Metadata.Drain * CurrentVehicle.Speed / 3000;
                        // increase consumption based on engine damage
                        DrainSpeed = DrainSpeed * ((1000 - CurrentVehicle.EngineHealth) / 1000) + DrainSpeed;
                        // actually remove the calculated value
                        CurrentVehicle.Metadata.Fuel -= DrainSpeed;
                        // avoid negative values
                        CurrentVehicle.Metadata.Fuel = (CurrentVehicle.Metadata.Fuel < 0.0f) ? 0.0f : CurrentVehicle.Metadata.Fuel;
                    }
                    // Draining enabled for helicopters?
                    else if (CurrentVehicle.Model.isHelicopter && EngineRunning && Settings.GetValueBool("HELIS", "MISC", true))
                    {
                        // Note: 254.921568627451f
                        // Note: 0.2 + ((speed * 0.2) / 5)
                        // Only take in account speed when accelerate xor reverse key is pressed.

                        // GamePad disabled or unavailable? Use keyboard!
                        if (GamePad == null)
                            if (Game.isGameKeyPressed(GameKey.MoveForward))
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * (.2f + ((CurrentVehicle.Speed * .2f) / 5.0f))) / 100.0f;
                            else
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * .208f) / 100.0f;
                        // Use the GamePad if available.
                        else if (GamePad.GetState().Gamepad.RightTrigger > 0.0f)
                            DrainSpeed = CurrentVehicle.Metadata.Drain * (((GamePad.GetState().Gamepad.RightTrigger * 100.0f) / 255.0f) / 10000.0f);
                        // Just go with it already.
                        else
                            DrainSpeed = (CurrentVehicle.Metadata.Drain * .208f) / 100.0f;

                        // Calculate the draining speed also taking engine damage to an account.
                        DrainSpeed = DrainSpeed * ((1000 - CurrentVehicle.EngineHealth) / 1000.0f) + DrainSpeed;
                        CurrentVehicle.Metadata.Fuel -= DrainSpeed;
                        CurrentVehicle.Metadata.Fuel = (CurrentVehicle.Metadata.Fuel < .0f) ? .0f : CurrentVehicle.Metadata.Fuel;
                    }
                    // Draining enabled for boats?
                    else if (CurrentVehicle.Model.isBoat && EngineRunning && Settings.GetValueBool("BOATS", "MISC", true))
                    {
                        // Note: 0.2 + ((speed * 0.2) / 5)
                        // Only take in account speed when accelerate xor reverse key is pressed.

                        // GamePad disabled or unavailable? Use keyboard!
                        if (GamePad == null)
                            if (Game.isGameKeyPressed(GameKey.MoveForward) ^ Game.isGameKeyPressed(GameKey.MoveBackward))
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * (.2f + ((CurrentVehicle.Speed * .2f) / 5.0f))) / 100;
                            else
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * .208f) / 100;
                        // Use the GamePad if available.
                        else
                            if (GamePad.GetState().Gamepad.RightTrigger > 0 ^ GamePad.GetState().Gamepad.LeftTrigger > 0)
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * (.2f + ((CurrentVehicle.Speed * .2f) / 5.0f))) / 100;
                            else
                                DrainSpeed = (CurrentVehicle.Metadata.Drain * .208f) / 100;

                        // Calculate the draining speed also taking engine damage to an account.
                        DrainSpeed = DrainSpeed * ((1000 - CurrentVehicle.EngineHealth) / 1000) + DrainSpeed;
                        CurrentVehicle.Metadata.Fuel -= DrainSpeed;
                        CurrentVehicle.Metadata.Fuel = (CurrentVehicle.Metadata.Fuel < .0f) ? .0f : CurrentVehicle.Metadata.Fuel;
                    }

                    // Enter to reserved fuel
                    if (!OnReserve && CurrentVehicle.Metadata.Fuel <= CurrentVehicle.Metadata.Reserve && EngineRunning && CurrentVehicle.Speed > 2.5f)
                    {
                        // Set as in reserve.
                        OnReserve = true;
                        Play("resSound");

                        // Let the player know.
                        if (Settings.GetValueBool("RESERVEDFUELTEXT", "TEXTS", true))
                        {
                            string reserveText = "Vehicle is running low on fuel!";
                            if (MaxFuelBottles - UsedFuelBottles >= 1)
                                reserveText += String.Format(" You have {0} emergency fuel bottle{1} left.", MaxFuelBottles - UsedFuelBottles, MaxFuelBottles - UsedFuelBottles == 1 ? "" : "s");
                            else
                                reserveText += " Drive to a refueling station quickly!";
                            AGame.PrintTextForever(reserveText);
                        }

                        // Log the situation.
                        Log("DrainFuel", "Player entered to reserved fuel on vehicle: " + CurrentVehicle.Name.ToString() + " with " + CurrentVehicle.Metadata.Fuel + " fuel units and " + (MaxFuelBottles - UsedFuelBottles) + " emergency fuel bottle" + (((MaxFuelBottles - UsedFuelBottles) == 1) ? "" : "s") + " left.");
                    }
                    // If vehicle has fuel than reserved amount.
                    else if (CurrentVehicle.Metadata.Fuel > CurrentVehicle.Metadata.Reserve)
                    {
                        // Then it's not on reserve... that's obvious!
                        OnReserve = false;
                    }
                }
                // Oh... bad luck! No fuel?!
                else
                {
                    // Stop the engine immediately!
                    CurrentVehicle.EngineRunning = false;
                    EngineRunning = false;

                    // Turn hazard lights on to assist the traffic!
                    // Fix for compatibility issue with Indicator Mod
                    AVehicle Veh = TypeConverter.ConvertToAVehicle(CurrentVehicle);
                    // just making sure all blinkers are active
                    MethodInfo method = Veh.GetType().GetMethod("IndicatorLight");
                    (method.Invoke(Veh, new object[] { 0 }) as AIndicatorLight).On = true;
                    (method.Invoke(Veh, new object[] { 1 }) as AIndicatorLight).On = true;
                    (method.Invoke(Veh, new object[] { 2 }) as AIndicatorLight).On = true;
                    (method.Invoke(Veh, new object[] { 3 }) as AIndicatorLight).On = true;
                    CurrentVehicle.HazardLightsOn = true;

                    // Set fuel level as zero for double sure. And to avoid the gauge bar be drawn backwards, ending up with a green line outside the gauge boundaries.
                    CurrentVehicle.Metadata.Fuel = 0;

                    // Smoking a little maybe? (only if he isn't damaged too much)
                    CurrentVehicle.EngineHealth = (CurrentVehicle.EngineHealth > 100.0f) ? 100.0f : CurrentVehicle.EngineHealth;
                    CurrentVehicle.Metadata.NoFuelDamage = (bool)true;

                    // I don't know how to explain this line, hahhha... Let's say it's a big dynamic text?
                    // This is shown when the vehicle ran out of fuel.
                    if (Settings.GetValueBool("OUTOFFUELTEXT", "TEXTS", true) && !ranOutMessageDisplayed)
                    {
                        // Shows when ran out of fuel. EDIT: I've trie to changed this to chained if's but for now it stays like that
                        AGame.PrintText((
                            ((MaxFuelBottles - UsedFuelBottles) >= 1)
                            ? ((CurrentVehicle.Speed == 0.0f)
                                ? "Press [" + Settings.GetValueKey("BOTTLEUSEKEY", "KEYS", Keys.U) + "] button to inject an emergency fuel bottle. " + ((((MaxFuelBottles - UsedFuelBottles) - 1) >= 1)
                                    ? "You have " + (MaxFuelBottles - UsedFuelBottles) + " fuel bottle" + (((MaxFuelBottles - UsedFuelBottles) == 1) ? "" : "s") + " left." + ((UsedFuelBottles > 0)
                                        ? "Refilling your " + UsedFuelBottles + " empty fuel bottle" + ((UsedFuelBottles == 1) ? "" : "s") + " costs $" + (UsedFuelBottles * FuelBottleCost) + " for you ($" + FuelBottleCost + " each)"
                                        : "A used fuel bottle can be refilled again for $" + FuelBottleCost + " at fueling stations") + "."
                                    : "Last fuel bottle, find a refueling station quickly!")
                                : "You ran out of fuel. " + (((MaxFuelBottles - UsedFuelBottles) >= 1)
                                ? "You have " + (MaxFuelBottles - UsedFuelBottles) + " emergency fuel bottle" + (((MaxFuelBottles - UsedFuelBottles) == 1) ? "" : "s") + " left."
                                : "No emerygency fuel bottles left.") + "Wait until the vehicle stops and engine is idle.")
                                : "Your vehicle ran out of fuel and you don't have any fuel bottles left." + ((CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike)
                            ? " You cannot start the vehicle without fuel. Call GET-555-FUEL or press [" + Settings.GetValueKey("SERVICEKEY", "KEYS", Keys.K) + "] to call emergency fuel service which costs $" + ServiceCost + "." : "" )));
                        ranOutMessageDisplayed = true;
                    }

                    // Log("DrainFuel", "Player ran out of fuel on vehicle: " + CurrentVehicle.Name.ToString() + " as " + CurrentVehicle.Metadata.Fuel + " fuel units and " + CurrentVehicle.Metadata.Reserve + " reserve units.");

                    // Mass angry when Niko lost a vehicle...
                    // NOTE: Something weired happening, exact thing runs. But last only around a second!
                    /*
                    if (CurrentVehicle.Metadata.Fuel == 0 && UsedFuelBottles == MaxFuelBottles)
                    {
                        Player.Character.SayAmbientSpeech("HIGH_FALL");
                        GTA.Native.Function.Call("SET_CAM_SHAKE", Game.DefaultCamera, true, 10);
                    }
                    */
                }

                // Is the player near a fueling station, then give him some help!
                if (isAtFuelStation() > -1)
                {
                    // Show currently owned cash so the player can decide whether to purchase fuel or not, or unit by unit.
                    GTA.Native.Function.Call("DISPLAY_CASH", true);

                    // Another big dynamic text which shows when player is inside of a fueling station radius.
                    if (Settings.GetValueBool("FUELINGSTATIONTEXT", "TEXTS", true))
                    {
                        if (!welcomeMessageDisplayed)
                        {
                            // Do we about to steal fuel? Are we near a fuel steal point?
                            if (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) > 0)
                            {
                                AGame.PrintTextForever(String.Format("You can steal fuel from the ~y~{0}~w~ by holding down [{1}]. Watch out for the ~b~cops~w~, you will get ~r~{2} stars~w~ by refueling here, also other people may attack you, be smart and quick!",
                                    Settings.GetValueString("NAME", StationName + isAtFuelStation(), "this refuel spot"),
                                    Settings.GetValueKey("REFUELKEY", "KEYS", Keys.E),
                                    Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0)));
                            }
                            // No? Hmmm... maybe just a fueling station then. No free fuel here.
                            else
                            {
                                string welcomeText = String.Format("Welcome to ~y~{0}~w~ Fuel Station. We offer fuel for ${1} a litre. Hold down the [{2}] button to refuel, a full tank will cost you ${3}. ",
                                    Settings.GetValueString("NAME", StationName + isAtFuelStation(), "This"),
                                    Settings.GetValueFloat("PRICE", "STATION" + isAtFuelStation(), 6.99f),
                                    Settings.GetValueKey("REFUELKEY", "KEYS", Keys.E),
                                    Convert.ToInt32(((CurrentVehicle.Metadata.MaxTank - CurrentVehicle.Metadata.Fuel) * Settings.GetValueFloat("PRICE", StationName + isAtFuelStation(), 6.99f))));
                                if (MaxFuelBottles - UsedFuelBottles < MaxFuelBottles)
                                {
                                    welcomeText += String.Format("Press [{0}] to buy a fuel bottle for ${1}, you can buy {2} more bottle{3}",
                                    Settings.GetValueKey("BOTTLEBUYKEY", "KEYS", Keys.B),
                                    FuelBottleCost,
                                    UsedFuelBottles,
                                    UsedFuelBottles == 1 ? "." : "s.");
                                }
                                AGame.PrintTextForever(welcomeText);
                            }
                            welcomeMessageDisplayed = true;
                        }
                    }

                    // Writing too much lines at the log is really annoying everytime you cross a square foot of a station!
                    // Log("DrainFuel", "Player entered to: " + Settings.GetValueString("NAME", station + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + " zone with vehicle: " + CurrentVehicle.Name.ToString() + ".");
                }

                #endregion
            }
            catch (Exception crap) { Log("ERROR: DrainFuel", crap.Message); }
        }
        /// <summary>
        /// Use if not sure if player is in vehicle.
        /// </summary>
        /// <returns></returns>
        private bool isAtFuelStationGeneric()
        {
            try
            {
                // Car and bike fueling stations
                for (int i = 1; i < 1000; i++)
                {
                    // Validate position.
                    Vector3 loc = Settings.GetValueVector3("LOCATION", "STATION" + i, new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                    if (loc.X == -123456789.0987654321f && loc.Y == -123456789.0987654321f && loc.Z == -123456789.0987654321f)
                        break;
                    // OK to proceed, calculate distance.
                    else if (loc.DistanceTo(Player.Character.Position) < Settings.GetValueFloat("RADIUS", "STATION" + i, 10))
                        return true;
                }

                // Boat fueling stations
                for (int i = 1; i < 1000; i++)
                {
                    // Validate position.
                    Vector3 loc = Settings.GetValueVector3("LOCATION", "BOATSTATION" + i, new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                    if (loc.X == -123456789.0987654321f && loc.Y == -123456789.0987654321f && loc.Z == -123456789.0987654321f)
                        break;
                    // OK to proceed, calculate distance.
                    else if (loc.DistanceTo(Player.Character.Position) < Settings.GetValueFloat("RADIUS", "BOATSTATION" + i, 10))
                        return true;
                }

                // Helicopter fueling stations
                for (int i = 1; i < 1000; i++)
                {
                    // Validate position.
                    Vector3 loc = Settings.GetValueVector3("LOCATION", "HELISTATION" + i, new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                    if (loc.X == -123456789.0987654321f && loc.Y == -123456789.0987654321f && loc.Z == -123456789.0987654321f)
                        break;
                    // OK to proceed, calculate distance.
                    else if (loc.DistanceTo(Player.Character.Position) < Settings.GetValueFloat("RADIUS", "HELISTATION" + i, 10))
                        return true;
                }

                // If everything gone wrong
                return false;
            }
            catch (Exception crap) { Log("ERROR: isAtFuelStationGeneric", crap.Message); return false; }
        }
        /// <summary>
        /// Use ONLY when player is in vehicle!
        ///
        /// Returns the station id, if the player is at any station valid for the vehicle type.
        /// </summary>
        /// <returns></returns>
        private int isAtFuelStation()
        {
            try
            {
                // Get the type of the fueling station according to the type of vehicle player is in.
                string toLookFor = (CurrentVehicle.Model.isHelicopter) ? "HELISTATION" : (CurrentVehicle.Model.isBoat) ? "BOATSTATION" : "STATION";
                for (int i = 1; i < 1000; i++)
                {
                    // Validate position.
                    Vector3 loc = Settings.GetValueVector3("LOCATION", toLookFor + i, new Vector3(-123456789.0987654321f, -123456789.0987654321f, -123456789.0987654321f));
                    if (loc.X == -123456789.0987654321f && loc.Y == -123456789.0987654321f && loc.Z == -123456789.0987654321f)
                        break;
                    // OK to proceed, calculate distance.
                    else if (loc.DistanceTo(Player.Character.Position) < Settings.GetValueFloat("RADIUS", "STATION" + i, 10))
                        return i;
                }
                return -1;
            }
            catch (Exception crap) { Log("ERROR: isAtFuelStation", crap.Message); return -1; }
        }
        /// <summary>
        /// Finishes the Refueling process.
        ///
        /// Debts the money from the player's money value, and allows the car to be started.
        /// </summary>
        private void FinishRefuel()
        {
            try
            {
                // Should player be refueling?
                if (Refueling)
                {
                    // If player is refueling from a public fueling station, deduct the cost.
                    if (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) == 0)
                    {
                        Player.Money -= Convert.ToInt32((RefuelAmount * Settings.GetValueFloat("PRICE", StationName + isAtFuelStation(), 6.99f)));
                    }

                    // If player should get a wanted level by refueling vehicle with a goverment property.
                    Player.WantedLevel = (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) > 0 && Player.WantedLevel < Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0)) ? Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) : Player.WantedLevel;

                    // Attack nearby peds to player for stealing fuel.
                    // Only affect for ground vehicles. It's not like people will crawl buildings to attack helicopters nor swim through ocean to attack boats? :D
                    if (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) > 0 && (CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike))
                    {
                        // Get random amount of nearby peds around 2-5...
                        foreach (Ped AttackingPed in World.GetPeds(CurrentVehicle.Position, 40.0f, new Random().Next(2, 5)))
                        {
                            // Exclude player in array!
                            if (AttackingPed != Player.Character)
                            {
                                // Become our mission character.
                                AttackingPed.BecomeMissionCharacter();
                                AttackingPed.BlockGestures = true;
                                AttackingPed.BlockPermanentEvents = true;

                                // Woah? Really? In our turf?
                                AttackingPed.SayAmbientSpeech("SURPRISED");

                                // Give them plenty of ammo...
                                AttackingPed.Weapons.MP5.Ammo = 800;

                                // Draw MP5 out of jackets... Muhahhha...
                                AttackingPed.Weapons.Select(Weapon.SMG_MP5);

                                // They hate me.
                                AttackingPed.ChangeRelationship(RelationshipGroup.Player, Relationship.Hate);

                                // Make him natorious.
                                AttackingPed.RelationshipGroup = RelationshipGroup.Criminal;
                                AttackingPed.CantBeDamagedByRelationshipGroup(RelationshipGroup.Criminal, false);

                                // Kill enemies first!
                                AttackingPed.PriorityTargetForEnemies = true;

                                // Can he climb, just over high obsctacles and still find path to the player?
                                AttackingPed.SetPathfinding(true, true, true);

                                // Don't allow him for his choice.
                                // AttackingPed.CanSwitchWeapons = false;
                                AttackingPed.BlockWeaponSwitching = true;

                                // Don't follow the player and attack.
                                AttackingPed.WillUseCarsInCombat = false;

                                // Don't let police bust them, because it's their property.
                                AttackingPed.WantedByPolice = false;

                                // Don't do old tasks... please...
                                AttackingPed.Task.ClearAll();

                                // New tasks first.
                                AttackingPed.Task.AlwaysKeepTask = true;

                                // SHOOT HIM!
                                AttackingPed.Task.ShootAt(Player, ShootMode.Continuous);
                                // AttackingPed.Task.FightAgainst(Player);

                                // We gotta set it now, no other choice... yet?
                                AttackingPed.NoLongerNeeded();
                            }
                        }
                    }

                    // Set as not refeuling... again...
                    Refueling = false;

                    // Startup engine and don't hotwire.
                    CurrentVehicle.EngineRunning = true;
                    EngineRunning = true;
                    CurrentVehicle.NeedsToBeHotwired = false;

                    CurrentVehicle.HazardLightsOn = false;

                    // Turn on lights if required.
                    GTA.Native.Function.Call("SET_VEH_LIGHTS", CurrentVehicle, 2);

                    // Display the balance.
                    GTA.Native.Function.Call("DISPLAY_CASH", true);

                    // In case if the player made it to the fueling station even when no fuel!
                    if (CurrentVehicle.EngineHealth <= 500.0f)
                    {
                        CurrentVehicle.EngineHealth = 1000.0f;
                    }

                    // Log about the wanted level increment.
                    if (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) > 0)
                    {
                        Log("FinishRefuel", "Player refueled vehicle: " + CurrentVehicle.Name.ToString() + " with " + RefuelAmount + " stolen fuel units at " + Settings.GetValueString("NAME", StationName + isAtFuelStation(), "Unknown") + " and got " + Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) + " star wanted level.");
                    }
                    // Log about the refuel.
                    else
                    {
                        AGame.PrintText(String.Format("Thank you for refueling at ~y~{0}~w~ Fuel Station. We loved your ${1}.", Settings.GetValueString("NAME", StationName + isAtFuelStation(), "this"), Convert.ToInt32(RefuelAmount * Settings.GetValueFloat("PRICE", StationName + isAtFuelStation(), 6.99f)))); 
                        //Game.DisplayText("You've refueled vehicle with " + Convert.ToInt32(RefuelAmount) + " fuel units for $" + Convert.ToInt32((RefuelAmount * Settings.GetValueFloat("PRICE", station + isAtFuelStation(), 6.99f))) + " at " + Settings.GetValueString("NAME", station + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + ".", 10000);
                        Log("FinishRefuel", "Player refueled vehicle: " + CurrentVehicle.Name.ToString() + " with " + RefuelAmount + " fuel units for $" + Convert.ToInt32((RefuelAmount * Settings.GetValueFloat("PRICE", StationName + isAtFuelStation(), 6.99f))) + " at " + Settings.GetValueString("NAME", StationName + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + ".");
                    }

                    RefuelAmount = 0.0f;
                }
            }
            catch (Exception crap) { Log("ERROR: FinishRefuel", crap.Message); }
        }
        /// <summary>
        /// Fills the fuel tank at 5 units per second in case of cars and bikes, fills at 10 units per second in case of boats and helis.
        /// </summary>
        private void ReFuel()
        {
            try
            {
                // Fill the tank of cars and bikes 5 unit per second, and for helicopters and boats fill tank 10 units per second.
                float unitsPerSecond = (CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike) ? 5 : 10;

                // Don't overrun maximum fuel tank capacity.
                if (CurrentVehicle.Metadata.Fuel >= CurrentVehicle.Metadata.MaxTank)
                {
                    // Take care of it.
                    CurrentVehicle.Metadata.Fuel = CurrentVehicle.Metadata.MaxTank;
                    FinishRefuel();
                }

                // If player tank is not full, he can refuel it.
                else
                {
                    // Calculate amount how much player costs for the fuel.
                    float amount = (CurrentVehicle.Metadata.Fuel + unitsPerSecond > CurrentVehicle.Metadata.MaxTank) ? CurrentVehicle.Metadata.MaxTank - CurrentVehicle.Metadata.Fuel : unitsPerSecond;

                    // If player doesn't have enough money to buy it.
                    if (Player.Money < (amount * Settings.GetValueFloat("PRICE", "STATION" + isAtFuelStation(), 6.99f)) + (RefuelAmount * Settings.GetValueFloat("PRICE", (CurrentVehicle.Model.isBoat) ? "BOATSTATION" : (CurrentVehicle.Model.isHelicopter) ? "HELISTATION" : "STATION" + isAtFuelStation(), 6.99f)))
                    {
                        // Player does not have any money
                        FinishRefuel();

                        // Inform the player about what happened.
                        if (Settings.GetValueBool("OUTOFFUNDSFUELTEXT", "TEXTS", true))
                        {
                            AGame.PrintText(String.Format("Sorry, but you don't have enough money to refuel your vehicle. Fuel costs ${0} per liter.", Settings.GetValueFloat("PRICE", "STATION" + isAtFuelStation(), 6.99f)));
                        }

                        // Log it.
                        Log("Refuel", "Player couldn't refuel vehicle: " + CurrentVehicle.Name.ToString() + " due to insufficient money at " + Settings.GetValueString("NAME", StationName + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + ".");
                    }
                    // If player have enough money to buy it.
                    else
                    {
                        // Add the requested fuel to vehicle's tank and deduct the cost.
                        CurrentVehicle.Metadata.Fuel += amount;
                        RefuelAmount += amount;

                        // Currently it's not much useful and it's kind of annoying find repeated similar lines at log file
                        // Log("Refuel", "Player refueling vehicle: " + CurrentVehicle.Name.ToString() + " with " + amount + " more units at " + Settings.GetValueString("NAME", station + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + " and now have " + CurrentVehicle.Metadata.Fuel + " units.");
                    }
                }
            }
            catch (Exception crap) { Log("ERROR: ReFuel", crap.Message); }
        }
        /// <summary>
        /// Play a specific sound from the embedded resources
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Play(string sound)
        {
            try
            {
                // Get the executing assembly.
                System.Reflection.Assembly a = System.Reflection.Assembly.GetExecutingAssembly();
                
                // Open the stream requested.
                System.IO.Stream s = a.GetManifestResourceStream("FuelScript.Resources."+ sound + ".wav");

                // Load the sound player and add the stream.
                SoundPlayer player = new SoundPlayer(s);

                // Play the stream.
                player.Play();
            }
            catch (Exception crap) { Log("ERROR: Play", crap.Message); }
        }
        #region Script Update Checking & Comparing
        /// <summary>
        /// Gets the latest file version from the project repository at Google Code
        /// </summary>
        /// <returns></returns>
        public string GetLatestVersion()
        {
            try
            {
                // Retreive version file from the repository
                return new WebClient().DownloadString(@"http:\\realistic-fuel-mod.googlecode.com/svn/trunk/FuelScript/FuelScript/Resources/version");
            }
            catch (Exception)
            {
                // Most likelly internet is down, more important we can't figure out any version
                return "server offline";
            }
        }
        /// <summary>
        /// Compares the latest release version with current running version.
        /// Returns true if the server is unavailable or if the current running version is higher or equal to the latest version
        /// </summary>
        /// <param name="CurrentFileVersion">variable version</param>
        /// <returns></returns>
        public bool isUpdated(string CurrentFileVersion)
        {
            // If this is a develpment verson, no reason to continue
            if (CurrentFileVersion.Contains("BETA"))
                return true;
            // Parse the version string
            CurrentFileVersion = CurrentFileVersion.Replace(".", "").Replace(" BETA", "").Trim();
            // Get latest version
            string LatestFileVersion = GetLatestVersion();
            // Server is offline, we will check another time
            if (LatestFileVersion == "server offline")
                return true;
            // Wow, everything cool compare the versions.
            return (Convert.ToInt32(CurrentFileVersion) > Convert.ToInt32(LatestFileVersion));
        }
        #endregion
        #endregion

        #region Key Bindings
        /// <summary>
        /// Handles the REFUELKEY's up event behaviour.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void FuelScript_KeyUp(object sender, GTA.KeyEventArgs e)
        {
            FinishRefuel();
        }
        /// <summary>
        /// Handles the REFUELKEY's down event behaviour.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void FuelScript_KeyDown(object sender, GTA.KeyEventArgs e)
        {
            // If player presses REFUELKEY, default E
            if (e.Key == Settings.GetValueKey("REFUELKEY", "KEYS", Keys.E))
            {
                try
                {
                    // If player is in vehicle at a fueling station and is not already refueling
                    if (!Refueling && Player.Character.isInVehicle() && isAtFuelStation() > -1)
                    {
                        // Set to refuel.
                        RefuelAmount = 0.0f;
                        Refueling = true;

                        // Let the player know.
                        if (Settings.GetValueBool("REFUELINGTEXT", "TEXTS", true))
                        {
                            // Are we refueling from a stealing point? To get bounty star? :D
                            if (Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) > 0)
                            {
                                AGame.PrintText(String.Format("You're now stealing fuel from the ~y~{0}~w~. Hold the button until it reaches to the amount you would like to steal.", Settings.GetValueString("NAME", StationName + isAtFuelStation(), "this location")));
                                
                                // Log as player stealing fuel from stealing point.
                                Log("KeyDown", "Player is now stealing fuel on: " + Settings.GetValueString("NAME", StationName + isAtFuelStation(), "Unknown") + " Stealing Point " + isAtFuelStation() + ", and about to gain " + Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) + " star wanted level.");
                            }
                            // OK, it's legal. Just a legal fueling station.
                            else
                            {
                                AGame.PrintText("You're vehicle is now being refueled by the fueling station. Hold the button until it reaches to the amount you would like to purchase.");

                                // Log as player using a fueling station.
                                Log("KeyDown", "Player is now using: " + Settings.GetValueString("NAME", StationName + isAtFuelStation(), "Unknown") + " Fueling Station " + isAtFuelStation() + ", which offers fuel for $" + Settings.GetValueFloat("PRICE", "STATION" + isAtFuelStation(), 6.99f) + " per unit.");
                            }
                        }
                    }
                }
                catch (Exception crap) { Log("ERROR: KeyDown[Refuel]", crap.Message); }
            }
            // If player presses BOTTLEUSEKEY, default U
            else if (e.Key == Settings.GetValueKey("BOTTLEUSEKEY", "KEYS", Keys.U))
            {
                try
                {
                    // If player ran out of fuel, and vehicle is stopped.
                    if (CurrentVehicle.Metadata.Fuel == 0 && CurrentVehicle.Speed == 0.0f)
                    {
                        // If player has at least one fuel bottle.
                        if ((MaxFuelBottles - UsedFuelBottles) >= 1)
                        {
                            // Say something as clue?
                            Player.Character.SayAmbientSpeech("START_CAR_PANIC");
                            Wait(2000);

                            // Start the repair by using the fuel bottle!
                            // Clear tasks...
                            Player.Character.Task.ClearAll();

                            // Focus on current tasks...
                            Player.Character.Task.AlwaysKeepTask = true;

                            // Get out of vehicle.
                            // If Niko is driving a Helicopter or a Boat we don't want to get him out to inject a fuel bottle, do we?
                            // That would kill Niko... lol, it could be fun though :D
                            // Added a fix for the crash when injecting fuel bottles to a bus by letting Niko do it inside!
                            if (CurrentVehicle.Model.isCar || CurrentVehicle.Model.isBike)
                            {
                                Player.Character.Task.LeaveVehicle(CurrentVehicle, true);

                                // Let him know that Niko doing a magic!
                                if (Settings.GetValueBool("BOTTLEUSINGTEXT", "TEXTS", true))
                                {
                                    ShowMessage("You're now using one of your emergency fuel bottles on this vehicle.", 10000);
                                }

                                // Wait until Niko got to the position.
                                while (Player.Character.isInVehicle())
                                {
                                    Wait(500);
                                }

                                // Wait until Niko got to the position.
                                while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", Player.Index))
                                {
                                    Wait(100);
                                }

                                // Turn to the vehicle side, door side!
                                Player.Character.Task.TurnTo(LastVehicle.Position);
                                Wait(500);

                                // Wait until Niko done turning.
                                while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", Player.Index))
                                {
                                    Wait(100);
                                }

                                // Do his magic!
                                Game.LocalPlayer.Character.Task.PlayAnimation(new AnimationSet("misstaxidepot"), "workunderbonnet", 4.0f);
                                Wait(6800);

                                // Repair the vehicle.
                                // Is the damage caused by low fuel running?
                                if (LastVehicle.Metadata.NoFuelDamage)
                                {
                                    // If so, repair the engine, not visual damage!
                                    LastVehicle.EngineHealth = 1000.0f;
                                }
                                // Is the damage caused by player's act?
                                else
                                {
                                    // If so, repair few of the damage in engine, not visual damage!
                                    LastVehicle.EngineHealth = (1000.0f - LastVehicle.EngineHealth) / 3;
                                }

                                // Give a little fuel capacity...
                                LastVehicle.Metadata.Fuel = LastVehicle.Metadata.MaxTank / 2;
                                // Not on reserve now...
                                OnReserve = false;

                                // Wait until Niko done animations.
                                while (!GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_FOR_AMBIENT_TASK", Player.Index))
                                {
                                    Wait(100);
                                }

                                // Startup the engine.
                                LastVehicle.EngineRunning = true;
                                LastVehicle.HazardLightsOn = false;
                                Player.Character.Task.EnterVehicle(LastVehicle, VehicleSeat.Driver);

                                // Wait until Niko get's back on vehicle if he's outside.
                                while (!Player.Character.isInVehicle() || Player.Character.CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver) != Player.Character)
                                {
                                    Wait(500);
                                }
                            }
                            // If it a helicopter, boat or a bus...
                            // Inject the fuel bottle without getting off the vehicle
                            // For safety issues... Hahhha...
                            else
                            {
                                // Let him know that Niko doing a magic!
                                if (Settings.GetValueBool("BOTTLEUSINGTEXT", "TEXTS", true))
                                {
                                    ShowMessage("You used one of your fuel bottles on this vehicle.", 5000);
                                }

                                // Repair the vehicle.
                                // Is the damage caused by low fuel running?
                                if (CurrentVehicle.Metadata.NoFuelDamage)
                                {
                                    // If so, repair the engine, not visual damage!
                                    CurrentVehicle.EngineHealth = 1000.0f;
                                }
                                // Is the damage caused by player's act?
                                else
                                {
                                    // If so, repair few of the damage in engine, not visual damage!
                                    CurrentVehicle.EngineHealth = (1000.0f - CurrentVehicle.EngineHealth) / 3;
                                }

                                // Give a little fuel capacity...
                                CurrentVehicle.Metadata.Fuel = CurrentVehicle.Metadata.Reserve + (CurrentVehicle.Metadata.MaxTank / 10);
                                // Not on reserve now...
                                OnReserve = false;

                                // Startup the engine.
                                CurrentVehicle.EngineRunning = true;
                                EngineRunning = true;
                                CurrentVehicle.HazardLightsOn = false;
                            }

                            // Relax a while...
                            Wait(2000);

                            // Let the player know...
                            // Game.DisplayText("You injected " + Convert.ToInt32(CurrentVehicle.Metadata.Fuel) + " litre(s) of fuel to your vehicle.", 6000);
                            Log("VehicleRepair", "Player injected " + Convert.ToInt32(CurrentVehicle.Metadata.Fuel) + " litre(s) of fuel for vehicle: " + CurrentVehicle.Name.ToString() + " with bottle " + (UsedFuelBottles + 1) + ".");

                            CurrentVehicle.Metadata.NoFuelDamage = (bool)false;

                            // Cost one fuel bottle...
                            UsedFuelBottles += 1;

                            // Hurry up, we wasted some time!
                            // Wait(600);
                            // Player.Character.SayAmbientSpeech("HURRY_UP");
                        }
                    }
                }
                catch (Exception crap) { Log("ERROR: KeyDown[BottleUse]", crap.Message); }
            }
            // If player presses BOTTLEBUYKEY, default B
            else if (e.Key == Settings.GetValueKey("BOTTLEBUYKEY", "KEYS", Keys.B))
            {
                try
                {
                    // If player haven't exceeded max fuel bottles limit and player is in vehicle at a fueling station.
                    // And make sure it isn't a fuel stealing point, because if so, player shouldn't allowed to steal fuel bottles. Just normal fuel only...
                    if ((MaxFuelBottles - UsedFuelBottles) < MaxFuelBottles && Player.Character.isInVehicle() && isAtFuelStation() > -1 && Settings.GetValueInteger("STARS", StationName + isAtFuelStation(), 0) < 0)
                    {
                        // Does the player have enough money to buy a fuel bottle?
                        if (Player.Money >= Convert.ToInt32(FuelBottleCost))
                        {
                            // Deduct from player's money.
                            Player.Money -= Convert.ToInt32(FuelBottleCost);
                            // Display the deduction.
                            GTA.Native.Function.Call("DISPLAY_CASH", true);

                            // Add one more bottle to player's inventory.
                            UsedFuelBottles -= 1;

                            // Let the player know.
                            if (Settings.GetValueBool("BOTTLEPURCHASETEXT", "TEXTS", true))
                            {
                                ShowMessage(String.Format("You purchased one more fuel bottle for ${0}. You now have {1} fuel bottle{2}", FuelBottleCost, MaxFuelBottles - UsedFuelBottles, MaxFuelBottles - UsedFuelBottles == 1 ? "": "s"), 5000);
                            }

                            Log("KeyDown", "Player purchased one more emergency fuel bottle on vehicle: " + CurrentVehicle.Name.ToString() + " and now have " + (MaxFuelBottles - UsedFuelBottles) + " out of " + MaxFuelBottles + " bottles.");
                        }
                        // If player doesn't have enough money
                        else
                        {
                            // Let the player know.
                            if (Settings.GetValueBool("OUTOFFUNDSBOTTLETEXT", "TEXTS", true))
                            {
                                AGame.PrintText("Sorry, you don't have enough money to buy a fuel bottle.");
                            }
                        }
                    }
                }
                catch (Exception crap) { Log("ERROR: KeyDown[BottleBuy]", crap.Message); }
            }
            // If player presses SERVICEKEY, default K.
            else if (e.Key == Settings.GetValueKey("SERVICEKEY", "KEYS", Keys.K))
            {
                try
                {
                    // Call to the same method when execute player calls to GET-555-FUEL
                    PhoneNumberHandler();
                }
                catch (Exception crap) { Log("ERROR: KeyDown[ServiceCall]", crap.Message); }
            }
        }
        #endregion

        #region Common Events
        /// <summary>
        /// run every second
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FuelScript_Tick(object sender, EventArgs e)
        {
            try
            {
                // Make sure Niko is in a vehicle and as driver.
                if (Player.Character.isInVehicle() && Player == CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver))
                {
                    // If player set as refueling...
                    if (Refueling)
                    {
                        // Make sure he does.
                        ReFuel();
                    }
                    // If he have plenty of fuel and just cruising around...
                    else
                    {
                        // Take care of fuel draining
                        // Vehicle required for mission?
                        if (CurrentVehicle.isRequiredForMission)
                        {
                            // User chosen to drain even so?
                            if (Settings.GetValueBool("MVDRAIN", "MISC", false))
                            {
                                // Drain fuel in mission required vehicles too then.
                                DrainFuel();
                            }
                        }
                        // Free roam vehicles?
                        else
                        {
                            // Free roam vehicles always should drain fuel.
                            DrainFuel();
                        }
                    }

                    // Don't know anything about a last vehicle?
                    if (LastVehicle == null || CurrentVehicle != LastVehicle)
                    {
                        // Set as not on reserve.
                        OnReserve = false;

                        // Here you go, get current vehicle as the last vehicle.
                        // It's not like player is glued to the vehicle right?
                        LastVehicle = CurrentVehicle;
                    }
                }
                // Niko is not in a vehicle?
                else
                {
                    // If so... yeah... no last vehicle.
                    LastVehicle = null;
                    // Clear any vehicle exclusive messages
                    welcomeMessageDisplayed = false;
                }

                // Track player vehicles details...
                if (Player.Character.isGettingIntoAVehicle)
                {
                    // Is he inside of a vehicle?
                    if (Player.Character.isInVehicle() && Player == CurrentVehicle.GetPedOnSeat(VehicleSeat.Driver))
                    {
                        // Log it so we can know what happened.
                        Log("Tick", "Player entered a vehicle: " + CurrentVehicle.Name.ToString() + ", Have " + Convert.ToInt32(CurrentVehicle.Metadata.Fuel) + " litre(s), Capacity - " + Convert.ToInt32(CurrentVehicle.Metadata.MaxTank) + " litre(s), Reserve - " + Convert.ToInt32(CurrentVehicle.Metadata.Reserve) + " litre(s), Drain - " + Convert.ToInt32(CurrentVehicle.Metadata.Drain) + " units.");

                        // Is current vehicle is required for an ingame mission?
                        if (CurrentVehicle.isRequiredForMission && !Settings.GetValueBool("MVDRAIN", "MISC", false))
                        {
                            // If so, fuel should not be drained, otherwise player will face so much trouble...
                            // Specially, if the mission is based on time, he can't go refuel it, can he?
                            if (Settings.GetValueBool("MISSIONREQUIREDTEXT", "TEXTS", true))
                            {
                                ShowMessage("Your vehicle is required for a mission, fuel is not draining!", 8000);
                            }

                            Log("DrainFuel", "Fuel is not draining on vehicle: " + CurrentVehicle.Name.ToString() + " as it's required for a mission.");
                        }
                        // Is it a normal vehicle? Using to free roam?
                        else
                        {
                            if (Settings.GetValueBool("VEHICLESTATUSTEXT", "TEXTS", true))
                            {
                                // Calculate fuel percentage.
                                float FuelAvailability = (Convert.ToInt32(CurrentVehicle.Metadata.Fuel) * 100) / Convert.ToInt32(CurrentVehicle.Metadata.MaxTank);

                                // When player gets into a vehicle, so it's status.
                                AGame.PrintTextForever(String.Format("This vehicle currently holds {0:00}% fuel left in it's {1} liter{2} tank.", CurrentVehicle.Metadata.Fuel * 100 / CurrentVehicle.Metadata.MaxTank, CurrentVehicle.Metadata.MaxTank, CurrentVehicle.Metadata.MaxTank == 1 ? "" : "s"));
                                if (MaxFuelBottles - UsedFuelBottles < 1)
                                     ShowMessage("No emergency fuel bottles left.", 5000);
                                else
                                    ShowMessage(String.Format(" You have {0} emergency fuel bottle{1} left.", MaxFuelBottles - UsedFuelBottles, MaxFuelBottles - UsedFuelBottles == 1 ? "" : "s"), 5000);
                            }

                            // Mark it as not damaged by low fuel running.
                            CurrentVehicle.Metadata.NoFuelDamage = (bool)false;
                        }
                    }
                }
            }
            // Log if any errors pops up...
            catch (Exception crap)
            {
                Log("ERROR: Tick", crap.Message);
            }
        }
        #endregion

        #region Pre Frame Drawing Functions
        /// <summary>
        /// run every frame, devMode
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FuelScript_PerFrameDrawing_devMode(object sender, GTA.GraphicsEventArgs e)
        {
            try
            {
                // Take care of lights and engine when refueling...
                if (Refueling)
                {
                    Player.Character.Task.ClearSecondary();
                    GTA.Native.Function.Call("FORCE_CAR_LIGHTS", CurrentVehicle, 0);
                    CurrentVehicle.EngineRunning = false;
                    EngineRunning = false;
                    // CurrentVehicle.HazardLightsOn = true;
                }

                // Set the scaling type.
                e.Graphics.Scaling = FontScaling.ScreenUnits;

                // Get the dashboard location.
                PointF Dashboard = new PointF(Settings.GetValueFloat("X", "DASHBOARD", 0.0f), Settings.GetValueFloat("Y", "DASHBOARD", 0.0f));

                // Is player in vehicle?
                if (Player.Character.isInVehicle())
                {
                    // Try to get the fuel level and draw it.
                    try { e.Graphics.DrawText("FUEL".PadRight(15) + CurrentVehicle.Metadata.Fuel, Dashboard.X, Dashboard.Y + 0.02f); }
                    catch { }

                    // Draw vehicle speed.
                    e.Graphics.DrawText("SPEED".PadRight(15) + "\t" + CurrentVehicle.Speed * 3.6f, Dashboard.X, Dashboard.Y + 0.04f);

                    // Draw vehicle engine health (0-1000 float).
                    e.Graphics.DrawText("ENGINE".PadRight(15) + "\t" + CurrentVehicle.EngineHealth, Dashboard.X, Dashboard.Y + 0.06f);

                    // Draw vehicle RPM (how hard the player push the engine).
                    e.Graphics.DrawText("RPM".PadRight(15) + "\t" + CurrentVehicle.Speed, Dashboard.X, Dashboard.Y + 0.08f);

                    // Draw vehicle hash code.
                    e.Graphics.DrawText("HASH".PadRight(15) + "\t" + CurrentVehicle.Model.Hash, Dashboard.X, Dashboard.Y + 0.1f);

                    // Draw vehicle's human friendly name.
                    e.Graphics.DrawText("NAME".PadRight(15) + "\t" + CurrentVehicle.Model, Dashboard.X, Dashboard.Y + 0.12f);

                    // Draw drain per second speed (how faster the fuel is draining per second).
                    e.Graphics.DrawText("DRAIN/Sec".PadRight(15) + "\t" + DrainSpeed, Dashboard.X, Dashboard.Y + 0.14f);

                    // Draw vehicle doors status.
                    e.Graphics.DrawText("DOOR".PadRight(15) + "\t" + ((CurrentVehicle.DoorLock == DoorLock.None) ? "UNLOCKED" : ((CurrentVehicle.DoorLock == DoorLock.CanOpenFromInside) ? "OUT LOCK" : "FULL LOCK")), Dashboard.X, Dashboard.Y + 0.16f);
                }

                // Draw player position. X, Y and Z coordinates.
                e.Graphics.DrawText("LOCATION", Dashboard.X, Dashboard.Y + 0.2f);
                e.Graphics.DrawText(Player.Character.Position.X + ", " + Player.Character.Position.Y + ", " + Player.Character.Position.Z, Dashboard.X, Dashboard.Y + 0.22f);

            }
            catch (Exception crap)
            {
                Log("ERROR: devMode", crap.Message);
            }
        }
        /// <summary>
        /// run every frame, digitalMode
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FuelScript_PerFrameDrawing_digitalMode(object sender, GTA.GraphicsEventArgs e)
        {
            try
            {
                // Take care of the lights and engine if player is refeuling..
                if (Refueling)
                {
                    GTA.Native.Function.Call("FORCE_CAR_LIGHTS", CurrentVehicle, 0);
                    CurrentVehicle.EngineRunning = false;
                    EngineRunning = false;
                    // CurrentVehicle.HazardLightsOn = true;
                }

                // Set the scaling type.
                e.Graphics.Scaling = FontScaling.ScreenUnits;

                // If player is in a vehicle.
                if (Player.Character.isInVehicle())
                {
                    // Draw fuel level.
                    try
                    {
                        e.Graphics.DrawText("FUEL", Dashboard.X, Dashboard.Y, Color.Beige);
                        e.Graphics.DrawText(
                            Convert.ToInt32((float)CurrentVehicle.Metadata.Fuel).ToString(),
                            Dashboard.X + 0.06f,
                            Dashboard.Y,
                            (CurrentVehicle.Metadata.Fuel <= CurrentVehicle.Metadata.Reserve)
                                ? Color.Red
                                : Color.Green);
                    }
                    catch { }

                    // Draw vehicle speed.
                    e.Graphics.DrawText("SPEED", Dashboard.X, Dashboard.Y + 0.03f);

                    // If current vehicle is a boat, units needs to be converted to knots.
                    if (CurrentVehicle.Model.isBoat)
                    {
                        e.Graphics.DrawText(Convert.ToInt32(CurrentVehicle.Speed * Knots).ToString(), Dashboard.X + 0.06f, Dashboard.Y + 0.03f);
                        e.Graphics.DrawText("Knots", Dashboard.X + 0.09f, Dashboard.Y + 0.03f);

                    }
                    // If it's a normal vehicle, draw it in KPH, or MPH.
                    else
                    {
                        e.Graphics.DrawText(Convert.ToInt32(CurrentVehicle.Speed * SpeedMultiplier).ToString(), Dashboard.X + 0.06f, Dashboard.Y + 0.03f);
                        e.Graphics.DrawText((SpeedMultiplier == 3.6f) ? "KPH" : "MPH", Dashboard.X + 0.09f, Dashboard.Y + 0.03f);
                    }
                }
            }
            catch (Exception crap)
            {
                Log("ERROR: digitalMode", crap.Message);
            }
        }
        /// <summary>
        /// run every frame, digitalMode
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FuelScript_PerFrameDrawing_classicMode(object sender, GTA.GraphicsEventArgs e)
        {
            try
            {
                // Take care of the lights and engine when refueling.
                if (Refueling)
                {
                    GTA.Native.Function.Call("FORCE_CAR_LIGHTS", CurrentVehicle, 0);
                    CurrentVehicle.EngineRunning = false;
                    EngineRunning = false;
                    // CurrentVehicle.HazardLightsOn = true;
                }

                // Set the scaling type.
                e.Graphics.Scaling = FontScaling.ScreenUnits;

                // If player is in a vehicle.
                if (Player.Character.isInVehicle())
                {
                    try
                    {
                        // Calculate fuel availablity...
                        float FuelAvailability = (CurrentVehicle.Metadata.Fuel * 100) / CurrentVehicle.Metadata.MaxTank;

                        // Draw the fuel bottles status (such as "2/5").
                        e.Graphics.DrawText(
                            String.Format("{0}/{1}", MaxFuelBottles - UsedFuelBottles, MaxFuelBottles),
                            Dashboard.X - 0.030f,
                            Dashboard.Y - 0.011f,
                            (MaxFuelBottles - UsedFuelBottles <= 1)
                                ? ((Flashing < 5)
                                    ? GTA.ColorIndex.SmokeSilverPoly
                                    : (GTA.ColorIndex)35)
                                : GTA.ColorIndex.SmokeSilverPoly, // at this point, if we have issues about performance the color can very well be select when any of the 3 conditions change in the first place
                            FuelMeterFont);

                        // Draw fuel level status (such as "57%").
                        e.Graphics.DrawText(
                            String.Format("{0:00}%", FuelAvailability),
                            (Dashboard.X + GaugeWidth) + 0.006f,
                            Dashboard.Y - 0.012f,
                            (CurrentVehicle.Metadata.Fuel <= CurrentVehicle.Metadata.Reserve)
                                ? ((Flashing < 5)
                                    ? GTA.ColorIndex.SmokeSilverPoly
                                    : (GTA.ColorIndex)35)
                                : GTA.ColorIndex.SmokeSilverPoly, // at this point, if we have issues about performance the color can very well be select when any of the 3 conditions change in the first place
                            FuelMeterFont);

                        // Draw fuel level meter's black background.
                        e.Graphics.DrawRectangle(
                            new RectangleF(
                                Dashboard.X - 0.0035f,
                                Dashboard.Y - 0.004f,
                                GaugeWidth, 0.0125f),
                            GTA.ColorIndex.Black);

                        // Draw fuel level meter's dark grey foreground.
                        e.Graphics.DrawRectangle(
                            new RectangleF(
                                Dashboard.X,
                                Dashboard.Y,
                                (1 * (GaugeWidth - 0.007f)) / 1, 0.006f),
                            (GTA.ColorIndex)1);

                        // Draw the front rectange widening how much fuel vehicle has.
                        // Green as normal, and red when running on reserved.
                        e.Graphics.DrawRectangle(
                            new RectangleF(
                                Dashboard.X,
                                Dashboard.Y,
                                (CurrentVehicle.Metadata.Fuel * (GaugeWidth - 0.008f)) / CurrentVehicle.Metadata.MaxTank,
                                0.006f),
                            (CurrentVehicle.Metadata.Fuel <= CurrentVehicle.Metadata.Reserve)
                                ? ((Flashing < 5)
                                    ? (GTA.ColorIndex)1
                                    : (GTA.ColorIndex)35
                                )
                            : (GTA.ColorIndex)50); // at this point, if we have issues about performance the color can very well be select when any of the 3 conditions change in the first place

                        // Controls the Flashinging when on reserved fuel.
                        // Strange, but it won't flash if we used Flashing++;
                        // It's not strange it is expected behaviour: Flashing++ uses the variable Flashing and updates it with 1. ++Flashing updates the variable with 1 and then uses it.
                        Flashing = (Flashing == 20) ? 0 : ++Flashing;
                    }
                    catch { }
                }

                // Draw Beta Watermark if the release is a beta release!
                #if DEBUG
                e.Graphics.DrawSprite(BetaMark, 0.07f, 0.06f, 0.11f, 0.085f, 0);
                #endif
            }
            catch (Exception crap)
            {
                Log("ERROR: classicMode", crap.Message);
            }
        }
        #endregion
    }
}