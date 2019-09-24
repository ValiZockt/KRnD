﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using UnityEngine;

namespace KRnD.Source
{

	[KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
	public class KRnD : MonoBehaviour
	{
		private static bool _initialized;
		public static Dictionary<string, PartStats> originalStats;
		public static Dictionary<string, PartUpgrades> upgrades = new Dictionary<string, PartUpgrades>();
		public static List<string> fuelResources;
		public static List<string> blacklistedParts;


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Is called when this Add-on is first loaded to initializes all values (eg registration of event-
		/// 		  handlers and creation of original-stats library).</summary>
		[UsedImplicitly]
		public void Awake()
		{
			try {

				// Execute the following code only once:
				if (_initialized) return;
				DontDestroyOnLoad(this);

				// Register event-handlers:
				GameEvents.onVesselChange.Add(OnVesselChange);
				GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);

				ValueConstants.Initialize();

				fuelResources = FetchAllFuelResources();
				blacklistedParts = FetchAllBlacklistedParts();
				originalStats = FetchAllPartStats();

				_initialized = true;
			} catch (Exception e) {
				Debug.LogError("[KRnD] Awake(): " + e);
			}
		}


		// Is called every time the active vessel changes (on entering a scene, switching the vessel or on docking).
		private void OnVesselChange(Vessel vessel)
		{
			try {
				UpdateVessel(vessel);
			} catch (Exception e) {
				Debug.LogError("[KRnD] OnVesselChange(): " + e);
			}
		}

		// Is called when we interact with a part in the editor.
		private void OnEditorPartEvent(ConstructionEventType ev, Part part)
		{
			try {
				if (ev != ConstructionEventType.PartCreated && ev != ConstructionEventType.PartDetached && ev != ConstructionEventType.PartAttached && ev != ConstructionEventType.PartDragging) return;
				KRnDUI.selectedPart = part;
			} catch (Exception e) {
				Debug.LogError("[KRnD] EditorPartEvent(): " + e);
			}
		}


		public static float CalculateImprovementFactor(float base_improvement, float improvement_scale, int upgrade_level)
		{
			float factor = 0;
			if (upgrade_level < 0) upgrade_level = 0;
			for (var i = 0; i < upgrade_level; i++) {
				if (i == 0) {
					factor += base_improvement;
				} else {
					factor += base_improvement * (float) Math.Pow(improvement_scale, i - 1);
				}
			}

			if (base_improvement < 0 && factor < -0.9) factor = -0.9f;
			return (float) Math.Round(factor, 4);
		}

		public static int CalculateScienceCost(int base_cost, float cost_scale, int upgrade_level)
		{
			float cost = 0;
			if (upgrade_level < 0) upgrade_level = 0;
			for (var i = 0; i < upgrade_level; i++) {
				if (i == 0) {
					cost = base_cost;
				} else {
					cost += base_cost * (float) Math.Pow(cost_scale, i - 1);
				}
			}

			if (cost > 2147483647) return 2147483647; // Cap at signed 32 bit int
			return (int) Math.Round(cost);
		}

		// Since KSP 1.1 the info-text of solar panels is not updated correctly, so we have use this workaround-function
		// to create our own text.
		public static string GetSolarPanelInfo(ModuleDeployableSolarPanel solar_module)
		{
			var info = solar_module.GetInfo();
			var charge_rate = solar_module.chargeRate * solar_module.efficiencyMult;
			var charge_string = charge_rate.ToString("0.####/s");
			var prefix = "<b>Electric Charge: </b>";
			return Regex.Replace(info, prefix + "[0-9.]+/[A-Za-z.]+", prefix + charge_string);
		}

		// Updates the global dictionary of available parts with the current set of upgrades (should be
		// executed for example when a new game starts or an existing game is loaded).
		public static int UpdateGlobalParts()
		{
			var upgrades_applied = 0;
			try {
				if (upgrades == null) throw new Exception("upgrades-dictionary missing");
				foreach (var part in PartLoader.LoadedPartsList) {
					try {
						PartUpgrades upgrade;
						if (!upgrades.TryGetValue(part.name, out upgrade)) upgrade = new PartUpgrades(); // If there are no upgrades, reset the part.

						// Update the part to its latest model:
						UpdatePart(part.partPrefab, true);

						// Rebuild the info-screen:
						var converter_module_number = 0; // There might be multiple modules of this type
						var engine_module_number = 0; // There might be multiple modules of this type
						foreach (var info in part.moduleInfos) {
							if (info.moduleName.ToLower() == "engine") {
								var engines = PartStats.GetEngineModules(part.partPrefab);
								if (engines != null && engines.Count > 0) {
									var engine = engines[engine_module_number];
									info.info = engine.GetInfo();
									info.primaryInfo = engine.GetPrimaryField();
									engine_module_number++;
								}
							} else if (info.moduleName.ToLower() == "rcs") {
								var rcs = PartStats.GetRcsModule(part.partPrefab);
								if (rcs) info.info = rcs.GetInfo();
							} else if (info.moduleName.ToLower() == "reaction wheel") {
								var reaction_wheel = PartStats.GetReactionWheelModule(part.partPrefab);
								if (reaction_wheel) info.info = reaction_wheel.GetInfo();
							} else if (info.moduleName.ToLower() == "deployable solar panel") {
								var solar_panel = PartStats.GetSolarPanelModule(part.partPrefab);
								if (solar_panel) info.info = GetSolarPanelInfo(solar_panel);
							} else if (info.moduleName.ToLower() == "landing leg") {
								var landing_leg = PartStats.GetLandingLegModule(part.partPrefab);
								if (landing_leg) info.info = landing_leg.GetInfo();
							} else if (info.moduleName.ToLower() == "fission generator") {
								var fission_generator = PartStats.GetFissionGeneratorModule(part.partPrefab);
								if (fission_generator) info.info = fission_generator.GetInfo();
							} else if (info.moduleName.ToLower() == "generator") {
								var generator = PartStats.GetGeneratorModule(part.partPrefab);
								if (generator) info.info = generator.GetInfo();


							} else if (info.moduleName.ToLower() == "data transmitter") {
								var antenna = PartStats.GetDataTransmitter(part.partPrefab);
								if (antenna) info.info = antenna.GetInfo();


							} else if (info.moduleName.ToLower() == "science lab") {
								var lab = PartStats.GetScienceLab(part.partPrefab);
								if (lab) info.info = lab.GetInfo();



							} else if (info.moduleName.ToLower() == "resource converter") {
								var converter_list = PartStats.GetConverterModules(part.partPrefab);
								if (converter_list != null && converter_list.Count > 0) {
									var converter = converter_list[converter_module_number];
									info.info = converter.GetInfo();
									converter_module_number++;
								}
							} else if (info.moduleName.ToLower() == "parachute") {
								var parachute = PartStats.GetParachuteModule(part.partPrefab);
								if (parachute) info.info = parachute.GetInfo();
							} else if (info.moduleName.ToLower() == "custom-built fairing") {
								var fairing = PartStats.GetFairingModule(part.partPrefab);
								if (fairing) info.info = fairing.GetInfo();
							}
						}

						var fuel_resources = PartStats.GetFuelResources(part.partPrefab);
						var electric_charge = PartStats.GetChargeResource(part.partPrefab);
						// The Resource-Names are not always formatted the same way, eg "Electric Charge" vs "ElectricCharge", so we do some reformatting.
						foreach (var info in part.resourceInfos) {
							if (electric_charge != null && info.resourceName.Replace(" ", "").ToLower() == electric_charge.resourceName.Replace(" ", "").ToLower()) {
								info.info = electric_charge.GetInfo();
								info.primaryInfo = "<b>" + info.resourceName + ":</b> " + electric_charge.maxAmount;
							} else if (fuel_resources != null) {
								foreach (var fuel_resource in fuel_resources) {
									if (info.resourceName.Replace(" ", "").ToLower() == fuel_resource.resourceName.Replace(" ", "").ToLower()) {
										info.info = fuel_resource.GetInfo();
										info.primaryInfo = "<b>" + info.resourceName + ":</b> " + fuel_resource.maxAmount;
										break;
									}
								}
							}
						}

						upgrades_applied++;
					} catch (Exception e) {
						Debug.LogError("[KRnD] updateGlobalParts(" + part.title + "): " + e);
					}
				}
			} catch (Exception e) {
				Debug.LogError("[KRnD] updateGlobalParts(): " + e);
			}

			return upgrades_applied;
		}

		// Updates all parts in the vessel that is currently active in the editor.
		public static void UpdateEditorVessel(Part root_part = null)
		{
			if (root_part == null) root_part = EditorLogic.RootPart;
			if (!root_part) return;
			UpdatePart(root_part, true); // Update to the latest model
			foreach (var child_part in root_part.children) {
				UpdateEditorVessel(child_part);
			}
		}

		// Updates the given part either to the latest model (updateToLatestModel=TRUE) or to the model defined by its
		// KRnDModule.
		public static void UpdatePart(Part part, bool update_to_latest_model)
		{
			PartUpgrades upgrades_to_apply;
			if (update_to_latest_model) {
				if (upgrades.TryGetValue(SanatizePartName(part.name), out upgrades_to_apply)) {
					// Apply upgrades from global list:
					UpdatePart(part, upgrades_to_apply);
				} else {
					// No Upgrades found, apply base-stats:
					upgrades_to_apply = new PartUpgrades();
					UpdatePart(part, upgrades_to_apply);
				}
			} else {
				// Extract current upgrades of the part and set those stats:
				var rnd_module = PartStats.GetKRnDModule(part);
				if (rnd_module != null && (upgrades_to_apply = rnd_module.GetCurrentUpgrades()) != null) {
					// Apply upgrades from the RnD-Module:
					UpdatePart(part, upgrades_to_apply);
				} else {
					// No Upgrades found, apply base-stats:
					upgrades_to_apply = new PartUpgrades();
					UpdatePart(part, upgrades_to_apply);
				}
			}
		}

		// Sometimes the name of the root-part of a vessel is extended by the vessel-name like "Mk1Pod (X-Bird)", this function can be used
		// as wrapper to always return the real name:
		public static string SanatizePartName(string part_name)
		{
			return Regex.Replace(part_name, @" \(.*\)$", "");
		}

		// Updates the given part with all upgrades provided in "upgradesToApply".
		public static void UpdatePart(Part part, PartUpgrades upgrades_to_apply)
		{
			try {
				// Find all relevant modules of this part:
				var rnd_module = PartStats.GetKRnDModule(part);
				if (rnd_module == null) return;
				if (upgrades == null) throw new Exception("upgrades-dictionary missing");
				if (KRnD.originalStats == null) throw new Exception("original-stats-dictionary missing");

				// Get the part-name ("):
				var part_name = SanatizePartName(part.name);

				// Get the original part-stats:
				PartStats original_stats;
				if (!originalStats.TryGetValue(part_name, out original_stats)) throw new Exception("no original-stats for part '" + part_name + "'");

				PartUpgrades latest_model;
				if (!upgrades.TryGetValue(part_name, out latest_model)) latest_model = null;


				// Dry Mass:
				rnd_module.dryMass_upgrades = upgrades_to_apply.dryMass;
				//var dry_mass_factor = 1 + CalculateImprovementFactor(rnd_module.dryMass_improvement, rnd_module.dryMass_improvementScale, upgrades_to_apply.dryMass);
				//part.mass = original_stats.dryMass * dry_mass_factor;
				//part.prefabMass = part.mass; // New in ksp 1.1, if this is correct is just guesswork however...
				UpgradeConstants u_constants = ValueConstants.GetData(StringConstants.DRY_MASS);
				part.prefabMass = part.mass = u_constants.CalculateImprovementValue(original_stats.dryMass, upgrades_to_apply.dryMass);

				// Dry Mass also improves fairing mass:
				var fairing_module = PartStats.GetFairingModule(part);
				if (fairing_module) {
					fairing_module.UnitAreaMass = u_constants.CalculateImprovementValue(original_stats.fairingAreaMass, upgrades_to_apply.dryMass);

					//fairng_module.UnitAreaMass = original_stats.fairingAreaMass * dry_mass_factor;
				}
				

				// Max Int/Skin Temp:
				rnd_module.maxTemperature_upgrades = upgrades_to_apply.maxTemperature;
#if true
				//UpgradeConstants u_constants = InitConstants.GetData(StringConstants.MAX_TEMPERATURE);
				//double upgrade_factor = u_constants.CalculateImprovementFactor(upgrades_to_apply.maxTemperature);
				//part.skinMaxTemp = original_stats.skinMaxTemp * upgrade_factor;
				//part.maxTemp = original_stats.intMaxTemp * upgrade_factor;

				part.skinMaxTemp = ValueConstants.GetData(StringConstants.MAX_TEMPERATURE).CalculateImprovementValue(original_stats.skinMaxTemp, upgrades_to_apply.maxTemperature);
				part.maxTemp = ValueConstants.GetData(StringConstants.MAX_TEMPERATURE).CalculateImprovementValue(original_stats.intMaxTemp, upgrades_to_apply.maxTemperature);

#else
				double temp_factor = 1 + CalculateImprovementFactor(rnd_module.maxTemperature_improvement, rnd_module.maxTemperature_improvementScale, upgrades_to_apply.maxTemperature);
				part.skinMaxTemp = original_stats.skinMaxTemp * temp_factor;
				part.maxTemp = original_stats.intMaxTemp * temp_factor;
#endif


				// Fuel Flow:
				u_constants = ValueConstants.GetData(StringConstants.FUEL_FLOW);
				float upgrade_factor = u_constants.CalculateImprovementFactor(upgrades_to_apply.fuelFlow);
				var engine_modules = PartStats.GetEngineModules(part);
				var rcs_module = PartStats.GetRcsModule(part);
				if (engine_modules != null || rcs_module) {
					rnd_module.fuelFlow_upgrades = upgrades_to_apply.fuelFlow;
					for (var i = 0; i < original_stats.maxFuelFlows.Count; i++) {
						//var max_fuel_flow = original_stats.maxFuelFlows[i] * (1 + CalculateImprovementFactor(rnd_module.fuelFlow_improvement, rnd_module.fuelFlow_improvementScale, upgrades_to_apply.fuelFlow));
						var max_fuel_flow = (float)(original_stats.maxFuelFlows[i] * upgrade_factor);
						if (engine_modules != null) {
							engine_modules[i].maxFuelFlow = max_fuel_flow;
						} else if (rcs_module) {
							rcs_module.thrusterPower = max_fuel_flow; // There is only one rcs-module
						}
					}
				} else {
					rnd_module.fuelFlow_upgrades = 0;
				}

				// ISP Vac & Atm:
				if (engine_modules != null || rcs_module) {
					rnd_module.ispVac_upgrades = upgrades_to_apply.ispVac;
					rnd_module.ispAtm_upgrades = upgrades_to_apply.ispAtm;
					var data_vac = ValueConstants.GetData(StringConstants.ISP_VAC);
					var data_atm = ValueConstants.GetData(StringConstants.ISP_ATM);
					var improvement_factor_vac = data_vac.CalculateImprovementFactor(upgrades_to_apply.ispVac);
					var improvement_factor_atm = data_atm.CalculateImprovementFactor(upgrades_to_apply.ispAtm);
					//var improvement_factor_vac = 1 + CalculateImprovementFactor(rnd_module.ispVac_improvement, rnd_module.ispVac_improvementScale, upgrades_to_apply.ispVac);
					//var improvement_factor_atm = 1 + CalculateImprovementFactor(rnd_module.ispAtm_improvement, rnd_module.ispAtm_improvementScale, upgrades_to_apply.ispAtm);

					for (var i = 0; i < original_stats.atmosphereCurves.Count; i++) {
						var is_airbreather = false;
						if (engine_modules != null) is_airbreather = engine_modules[i].engineType == EngineType.Turbine || engine_modules[i].engineType == EngineType.Piston || engine_modules[i].engineType == EngineType.ScramJet;
						var fc = new FloatCurve();
						for (var v = 0; v < original_stats.atmosphereCurves[i].Curve.length; v++) {
							var frame = original_stats.atmosphereCurves[i].Curve[v];

							var pressure = frame.time;
							float factor_at_this_pressure = 1;
							if (is_airbreather && original_stats.atmosphereCurves[i].Curve.length == 1) {
								factor_at_this_pressure = improvement_factor_atm; // Air-breathing engines have a pressure curve starting at 0, but they should use Atm. as improvement factor.
							} else if (pressure == 0) {
								factor_at_this_pressure = improvement_factor_vac; // In complete vacuum
							} else if (pressure >= 1) {
								factor_at_this_pressure = improvement_factor_atm; // At lowest kerbal atmosphere
							} else {
								factor_at_this_pressure = (1 - pressure) * improvement_factor_vac + pressure * improvement_factor_atm; // Mix both
							}

							var new_value = frame.value * factor_at_this_pressure;
							fc.Add(pressure, new_value);
						}

						if (engine_modules != null) {
							engine_modules[i].atmosphereCurve = fc;
						} else if (rcs_module) rcs_module.atmosphereCurve = fc; // There is only one rcs-module
					}
				} else {
					rnd_module.ispVac_upgrades = 0;
					rnd_module.ispAtm_upgrades = 0;
				}

				// Torque:
				var reaction_wheel = PartStats.GetReactionWheelModule(part);
				if (reaction_wheel) {
					rnd_module.torque_upgrades = upgrades_to_apply.torqueStrength;

					u_constants = ValueConstants.GetData(StringConstants.TORQUE);
					float torque =  u_constants.CalculateImprovementValue(original_stats.torqueStrength, upgrades_to_apply.torqueStrength);

					//var torque = original_stats.torqueStrength * (1 + CalculateImprovementFactor(rnd_module.torque_improvement, rnd_module.torque_improvementScale, upgrades_to_apply.torque));
					reaction_wheel.PitchTorque = torque;
					reaction_wheel.YawTorque = torque;
					reaction_wheel.RollTorque = torque;
				} else {
					rnd_module.torque_upgrades = 0;
				}

				// Charge Rate:
				var solar_panel = PartStats.GetSolarPanelModule(part);
				if (solar_panel) {
					rnd_module.chargeRate_upgrades = upgrades_to_apply.chargeRate;

					u_constants = ValueConstants.GetData(StringConstants.CHARGE_RATE);
					solar_panel.efficiencyMult = u_constants.CalculateImprovementValue(0, upgrades_to_apply.chargeRate);

					//var charge_efficiency = 1 + CalculateImprovementFactor(rnd_module.chargeRate_improvement, rnd_module.chargeRate_improvementScale, upgrades_to_apply.chargeRate);
					// Somehow changing the charge-rate stopped working in KSP 1.1, so we use the efficiency instead. This however does not
					// show up in the module-info (probably a bug in KSP), which is why we have another workaround to update the info-texts.
					// float chargeRate = originalStats.chargeRate * chargeEfficiency;
					// solarPanel.chargeRate = chargeRate;
					//solar_panel.efficiencyMult = charge_efficiency;
				} else {
					rnd_module.chargeRate_upgrades = 0;
				}

				// Crash Tolerance (only for landing legs):
				var landing_leg = PartStats.GetLandingLegModule(part);
				if (landing_leg) {

					rnd_module.crashTolerance_upgrades = upgrades_to_apply.crashTolerance;

					u_constants = ValueConstants.GetData(StringConstants.CRASH_TOLERANCE);
					part.crashTolerance = u_constants.CalculateImprovementValue(original_stats.crashTolerance, upgrades_to_apply.crashTolerance);
					//var crash_tolerance = original_stats.crashTolerance * (1 + CalculateImprovementFactor(rnd_module.crashTolerance_improvement, rnd_module.crashTolerance_improvementScale, upgrades_to_apply.crashTolerance));
					//part.crashTolerance = crash_tolerance;
				} else {
					rnd_module.crashTolerance_upgrades = 0;
				}

				// Battery Charge:
				var electric_charge = PartStats.GetChargeResource(part);
				if (electric_charge != null) {
					rnd_module.batteryCharge_upgrades = upgrades_to_apply.batteryCharge;

					u_constants = ValueConstants.GetData(StringConstants.BATTERY_CHARGE);
					var battery_charge = u_constants.CalculateImprovementValue(original_stats.batteryCharge, upgrades_to_apply.batteryCharge);
					//var battery_charge = original_stats.batteryCharge * (1 + CalculateImprovementFactor(rnd_module.batteryCharge_improvement, rnd_module.batteryCharge_improvementScale, upgrades_to_apply.batteryCharge));
					battery_charge = Math.Round(battery_charge); // We don't want half units of electric charge

					bool battery_is_full = Math.Abs(electric_charge.amount - electric_charge.maxAmount) < float.Epsilon;

					electric_charge.maxAmount = battery_charge;
					if (battery_is_full) electric_charge.amount = electric_charge.maxAmount;
				} else {
					rnd_module.batteryCharge_upgrades = 0;
				}

				// Generator & Fission-Generator Efficiency:
				var generator = PartStats.GetGeneratorModule(part);
				var fission_generator = PartStats.GetFissionGeneratorModule(part);
				if (generator || fission_generator) {
					rnd_module.generatorEfficiency_upgrades = upgrades_to_apply.generatorEfficiency;

					u_constants = ValueConstants.GetData(StringConstants.GENERATOR_EFFICIENCY);

					if (generator) {
						foreach (var output_resource in generator.resHandler.outputResources) {
							if (!original_stats.generatorEfficiency.TryGetValue(output_resource.name, out var original_rate)) continue;
							output_resource.rate = u_constants.CalculateImprovementValue(original_rate, upgrades_to_apply.generatorEfficiency);
							//output_resource.rate = (float) (original_rate * (1 + CalculateImprovementFactor(rnd_module.generatorEfficiency_improvement, rnd_module.generatorEfficiency_improvementScale, upgrades_to_apply.generatorEfficiency)));
						}
					}

					if (fission_generator) {
						var power_generation = u_constants.CalculateImprovementValue(original_stats.fissionPowerGeneration, upgrades_to_apply.generatorEfficiency);
						//var power_generation = original_stats.fissionPowerGeneration * (1 + CalculateImprovementFactor(rnd_module.generatorEfficiency_improvement, rnd_module.generatorEfficiency_improvementScale, upgrades_to_apply.generatorEfficiency));
						PartStats.SetGenericModuleValue(fission_generator, "PowerGeneration", power_generation);
					}
				} else {
					rnd_module.generatorEfficiency_upgrades = 0;
				}

				// Converter Efficiency:
				var converter_list = PartStats.GetConverterModules(part);
				if (converter_list != null) {
					u_constants = ValueConstants.GetData(StringConstants.CONVERTER_EFFICIENCY);


					foreach (var converter in converter_list) {
						if (!original_stats.converterEfficiency.TryGetValue(converter.ConverterName, out var original_output_resources)) continue;

						rnd_module.converterEfficiency_upgrades = upgrades_to_apply.converterEfficiency;
						// Since KSP 1.2 this can't be done in a foreach anymore, we have to read and write back the entire ResourceRatio-Object:
						for (var i = 0; i < converter.outputList.Count; i++) {
							var resource_ratio = converter.outputList[i];
							if (!original_output_resources.TryGetValue(resource_ratio.ResourceName, out var original_ratio)) continue;

							resource_ratio.Ratio = u_constants.CalculateImprovementValue(original_ratio, upgrades_to_apply.converterEfficiency);
//							resource_ratio.Ratio = (float) (original_ratio * (1 + CalculateImprovementFactor(rnd_module.converterEfficiency_improvement, rnd_module.converterEfficiency_improvementScale, upgrades_to_apply.converterEfficiency)));

							converter.outputList[i] = resource_ratio;
						}
					}
				} else {
					rnd_module.converterEfficiency_upgrades = 0;
				}


				// Antenna
				var antenna = PartStats.GetDataTransmitter(part);
				if (antenna) {
					rnd_module.antennaPower_upgrades = upgrades_to_apply.antennaPower;
					antenna.antennaPower = ValueConstants.GetData(StringConstants.ANTENNA_POWER).CalculateImprovementValue(original_stats.antennaPower, upgrades_to_apply.antennaPower);

					rnd_module.packetSize_upgrades = upgrades_to_apply.packetSize;
					antenna.packetSize = ValueConstants.GetData(StringConstants.PACKET_SIZE).CalculateImprovementValue(original_stats.packetSize, upgrades_to_apply.packetSize);
				}

				var science_lab = PartStats.GetScienceLab(part);
				if (science_lab) {
					rnd_module.dataStorage_upgrades = upgrades_to_apply.dataStorage;
					science_lab.dataStorage = ValueConstants.GetData(StringConstants.DATA_STORAGE).CalculateImprovementValue(original_stats.dataStorage, upgrades_to_apply.dataStorage);
				}



				// Parachute Strength:
				var parachute = PartStats.GetParachuteModule(part);
				if (parachute) {
					rnd_module.parachuteStrength_upgrades = upgrades_to_apply.parachuteStrength;

					u_constants = ValueConstants.GetData(StringConstants.PARACHUTE_STRENGTH);
					var chute_max_temp = original_stats.chuteMaxTemp * u_constants.CalculateImprovementFactor(upgrades_to_apply.parachuteStrength);
					//var chute_max_temp = original_stats.chuteMaxTemp * (1 + CalculateImprovementFactor(rnd_module.parachuteStrength_improvement, rnd_module.parachuteStrength_improvementScale, upgrades_to_apply.parachuteStrength));
					parachute.chuteMaxTemp = chute_max_temp; // The safe deployment-speed is derived from the temperature
				} else {
					rnd_module.parachuteStrength_upgrades = 0;
				}

				// Fuel Capacity:
				var fuel_resources = PartStats.GetFuelResources(part);
				if (fuel_resources != null && original_stats.fuelCapacities != null) {
					rnd_module.fuelCapacity_upgrades = upgrades_to_apply.fuelCapacity;

					u_constants = ValueConstants.GetData(StringConstants.FUEL_CAPACITY);


					double improvement_factor = u_constants.CalculateImprovementFactor(upgrades_to_apply.fuelCapacity);
					//double improvement_factor = 1 + CalculateImprovementFactor(rnd_module.fuelCapacity_improvement, rnd_module.fuelCapacity_improvementScale, upgrades_to_apply.fuelCapacity);

					foreach (var fuel_resource in fuel_resources) {
						if (!original_stats.fuelCapacities.ContainsKey(fuel_resource.resourceName)) continue;
						var original_capacity = original_stats.fuelCapacities[fuel_resource.resourceName];
						var new_capacity = original_capacity * improvement_factor;
						new_capacity = Math.Round(new_capacity); // We don't want half units of fuel

						bool tank_is_full = Math.Abs(fuel_resource.amount - fuel_resource.maxAmount) < float.Epsilon;

						fuel_resource.maxAmount = new_capacity;
						if (tank_is_full) fuel_resource.amount = fuel_resource.maxAmount;
					}
				} else {
					rnd_module.fuelCapacity_upgrades = 0;
				}
			} catch (Exception e) {
				Debug.LogError("[KRnD] updatePart(" + part.name + "): " + e);
			}
		}

		// Updates all parts of the given vessel according to their RnD-Module settings (should be executed
		// when the vessel is loaded to make sure, that the vessel uses its own, historic upgrades and not
		// the global part-upgrades).
		public static void UpdateVessel(Vessel vessel)
		{
			try {
				if (!vessel.isActiveVessel) return; // Only the currently active vessel matters, the others are not simulated anyway.
				if (upgrades == null) throw new Exception("upgrades-dictionary missing");
				//Debug.Log("[KRnD] updating vessel '" + vessel.vesselName.ToString() + "'");

				// Iterate through all parts:
				foreach (var part in vessel.parts) {
					// We only have to update parts which have the RnD-Module:
					var rnd_module = PartStats.GetKRnDModule(part);
					if (rnd_module == null) continue;

					if (vessel.situation == Vessel.Situations.PRELAUNCH) {
						// Update the part with the latest model while on the launchpad:
						UpdatePart(part, true);
					} else if (rnd_module.upgradeToLatest > 0) {
						// Flagged by another mod (eg KSTS) to get updated to the latest model (once):
						//Debug.Log("[KRnD] part '"+ KRnD.sanatizePartName(part.name) + "' of '"+ vessel.vesselName + "' was flagged to be updated to the latest model");
						rnd_module.upgradeToLatest = 0;
						UpdatePart(part, true);
					} else {
						// Update this part with its own stats:
						UpdatePart(part, false);
					}
				}
			} catch (Exception e) {
				Debug.LogError("[KRnD] updateVesselActive(): " + e);
			}
		}


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Loads blacklisted module list from the blacklist.cfg file.</summary>
		///
		/// <returns> The blacklisted modules.</returns>
		public List<string> LoadBlacklistedModules()
		{
			var blacklisted_modules = new List<string>();
			try {
				var node = ConfigNode.Load(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/" + StringConstants.BLACKLIST_FILENAME);

				foreach (var blacklisted_module in node.GetValues("BLACKLISTED_MODULE")) {
					if (!blacklisted_modules.Contains(blacklisted_module)) {
						blacklisted_modules.Add(blacklisted_module);
					}
				}
			} catch (Exception e) {
				Debug.LogError("[KRnD] getBlacklistedModules(): " + e);
			}

			return blacklisted_modules;
		}


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Loads blacklisted parts from blacklist.cfg file.</summary>
		///
		/// <returns> The blacklisted parts.</returns>
		public List<string> LoadBlacklistedParts()
		{
			var blacklisted_parts = new List<string>();
			try {
				var node = ConfigNode.Load(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/" + StringConstants.BLACKLIST_FILENAME);

				foreach (var blacklisted_part in node.GetValues("BLACKLISTED_PART")) {
					if (!blacklisted_parts.Contains(blacklisted_part)) {
						blacklisted_parts.Add(blacklisted_part);
					}
				}
			} catch (Exception e) {
				Debug.LogError("[KRnD] getBlacklistedParts(): " + e);
			}

			return blacklisted_parts;
		}


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Create a backup of all unmodified parts before we update them. We will later use these backup-
		/// 		  parts for all calculations of upgraded stats.</summary>
		///
		/// <returns> all part statistics.</returns>
		private static Dictionary<string, PartStats> FetchAllPartStats()
		{
			var original_stats = new Dictionary<string, PartStats>();
			foreach (var a_part in PartLoader.LoadedPartsList) {
				var part = a_part.partPrefab;

				// Backup this part, if it has the RnD-Module:
				if (PartStats.GetKRnDModule(part) == null) continue;

				if (!original_stats.ContainsKey(part.name)) {
					original_stats.Add(part.name, new PartStats(part));
				}
			}

			return original_stats;
		}


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Fetches all blacklisted parts. Start with the pre-defined list and then add in any part that
		/// 		  contains a blacklisted module.</summary>
		///
		/// <returns> All blacklisted parts.</returns>
		private List<string> FetchAllBlacklistedParts()
		{
			// Create a list of blacklisted parts (parts with known incompatible modules of other mods):
			List<string> blacklisted_parts = LoadBlacklistedParts();
			var blacklisted_modules = LoadBlacklistedModules();

			foreach (var a_part in PartLoader.LoadedPartsList) {
				var part = a_part.partPrefab;
				var should_blacklist = false;

				foreach (var part_module in part.Modules) {
					if (!blacklisted_modules.Contains(part_module.moduleName)) continue;
					should_blacklist = true;
					break;
				}

				if (!should_blacklist) continue;
				if (!blacklisted_parts.Contains(part.name)) {
					blacklisted_parts.Add(part.name);
				}
			}

			return blacklisted_parts;
		}


		///////////////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary> Fetches all fuel resources by going through every part and collecting all resources that are
		/// 		  listed as a propellant. If the propellent is listed in the NON_FUELS list, then don't
		/// 		  consider it a real fuel so don't add it.</summary>
		///
		/// <returns> All fuel resources that are not specifically indicated as a non-fuel propellant.</returns>
		private static List<string> FetchAllFuelResources()
		{
			// Create a list of all valid fuel resources: Always use MonoPropellant as fuel (RCS-Thrusters don't have engine modules and are not found with the code below)
			var fuel_resources = new List<string> { "MonoPropellant" };

			foreach (var a_part in PartLoader.LoadedPartsList) {
				var part = a_part.partPrefab;
				var engine_modules = PartStats.GetEngineModules(part);
				if (engine_modules == null) continue;
				foreach (var engine_module in engine_modules) {
					if (engine_module.propellants == null) continue;
					foreach (var propellant in engine_module.propellants) {

						// Don't consider a propellant to actually be a fuel if it is specifically part of the non-fuel list.
						if (StringConstants.NON_FUELS.Contains(propellant.name)) continue;

						//if (propellant.name == "ElectricCharge") continue; // Electric Charge is improved by batteries.
						//if (propellant.name == "IntakeAir") continue; // This is no real fuel-type.
						//if (propellant.name == "IntakeAtm") continue; // This is no real fuel-type.
						if (!fuel_resources.Contains(propellant.name)) fuel_resources.Add(propellant.name);
					}
				}
			}

#if false
				var list_string = "";
				foreach (var fuel_name in fuel_resources) {
					if (list_string != "") list_string += ", ";
					list_string += fuel_name;
				}

				Debug.Log("[KRnD] found " + KRnD.fuel_resources.Count.ToString() + " propellants: " + listString);
#endif

			return fuel_resources;
		}
	}
}