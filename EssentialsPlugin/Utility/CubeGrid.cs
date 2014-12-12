﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using VRageMath;

using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;

using SEModAPIInternal.API.Entity;
using SEModAPIInternal.API.Entity.Sector.SectorObject;
using SEModAPIInternal.API.Entity.Sector.SectorObject.CubeGrid;
using SEModAPIInternal.API.Entity.Sector.SectorObject.CubeGrid.CubeBlock;

using SEModAPIInternal.API.Common;

namespace EssentialsPlugin.Utility
{
	public enum RemoveGridTypes
	{
		All,
		Ships,
		Stations
	}

	public static class CubeGrids
	{
		public static Vector3D RemoveGridsInSphere(ulong userId, Vector3D startPosition, float radius, RemoveGridTypes removeType)
		{
			List<MyObjectBuilder_CubeGrid> gridsToMove = new List<MyObjectBuilder_CubeGrid>();
			BoundingSphereD sphere = new BoundingSphereD(startPosition, radius);
			List<IMyEntity> entitiesToMove = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
			List<IMyEntity> entitiesToRemove = new List<IMyEntity>();
			int count = 0;

			Wrapper.GameAction(() =>
			{
				foreach (IMyEntity entity in entitiesToMove)
				{
					if (!(entity is IMyCubeGrid))
						continue;

					IMyCubeGrid grid = (IMyCubeGrid)entity;
					MyObjectBuilder_CubeGrid cubeGrid = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
					if (removeType == RemoveGridTypes.Ships && cubeGrid.IsStatic)
						continue;

					if (removeType == RemoveGridTypes.Stations && !cubeGrid.IsStatic)
						continue;

					entitiesToRemove.Add(entity);
					Communication.SendPrivateInformation(userId, string.Format("Deleting entity '{0}' at {1}", entity.DisplayName, General.Vector3DToString(entity.GetPosition())));
					count++;
				}
			});

			for (int r = entitiesToRemove.Count - 1; r >= 0; r--)
			{
				IMyEntity entity = entitiesToRemove[r];
				//MyAPIGateway.Entities.RemoveEntity(entity);
				CubeGridEntity gridEntity = new CubeGridEntity((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder(), entity);
				gridEntity.Dispose();
			}

			Communication.SendPrivateInformation(userId, string.Format("Total entities removed: {0}", count));
			return startPosition;
		}

		public static MyPositionAndOrientation CreatePositionAndOrientation(Vector3 position, Vector3 lookAt)
		{
            Vector3 newForward = Vector3.Normalize(position - lookAt);
            Quaternion rotate = MathUtility.GetRotation(Vector3.Forward, newForward, Vector3.Up);
            Matrix rot = Matrix.CreateFromQuaternion(rotate);
            return new MyPositionAndOrientation(position, rot.Forward, rot.Up);
		}

		public static void GetBlocksUnconnected(HashSet<IMyEntity> connectedList, HashSet<IMyEntity> entitiesToConfirm)
		{
			foreach (IMyEntity entity in entitiesToConfirm)
			{
				if (!(entity is IMyCubeGrid))
					continue;

				IMyCubeGrid grid = (IMyCubeGrid)entity;
				MyObjectBuilder_CubeGrid gridBuilder = null;
				try
				{
					 gridBuilder = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
				}
				catch
				{
					continue;
				}

				bool result = false;
				foreach (MyObjectBuilder_CubeBlock block in gridBuilder.CubeBlocks)
				{
					if (block.TypeId == typeof(MyObjectBuilder_ShipConnector))
					{
						MyObjectBuilder_ShipConnector connector = (MyObjectBuilder_ShipConnector)block;
						if (connector.Connected)
						{
							IMyEntity connectedEntity = null;
							MyAPIGateway.Entities.TryGetEntityById(connector.ConnectedEntityId, out connectedEntity);

							if (connectedEntity != null)
							{
								result = true;
								break;
							}
						}
					}

					if (block.TypeId == typeof(MyObjectBuilder_PistonBase))
					{
						result = true;
						break;
					}

					if (block.TypeId == typeof(MyObjectBuilder_ExtendedPistonBase))
					{
						result = true;
						break;
					}

					if (block.TypeId == typeof(MyObjectBuilder_PistonTop))
					{
						result = true;
						break;
					}

					if (block.TypeId == typeof(MyObjectBuilder_MotorAdvancedStator))
					{
						MyObjectBuilder_MotorAdvancedStator stator = (MyObjectBuilder_MotorAdvancedStator)block;
						if (stator.RotorEntityId != 0)
						{
							IMyEntity connectedEntity = null;
							MyAPIGateway.Entities.TryGetEntityById(stator.RotorEntityId, out connectedEntity);

							if (connectedEntity != null)
							{
								result = true;
								break;
							}
						}
					}

					if (block.TypeId == typeof(MyObjectBuilder_MotorAdvancedRotor))
					{
						result = true;
						break;
					}

					if (block.TypeId == typeof(MyObjectBuilder_MotorStator))
					{
						MyObjectBuilder_MotorStator stator = (MyObjectBuilder_MotorStator)block;
						if (stator.RotorEntityId != 0)
						{
							IMyEntity connectedEntity = null;
							MyAPIGateway.Entities.TryGetEntityById(stator.RotorEntityId, out connectedEntity);

							if (connectedEntity != null)
							{
								result = true;
								break;
							}
						}
					}

					if (block.TypeId == typeof(MyObjectBuilder_MotorRotor))
					{
						result = true;
						break;
					}
				}

				if (!result)
					connectedList.Add(entity);
			}
		}

		public static IMyEntity Find(string displayName)
		{
			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();

			Wrapper.GameAction(() =>
			{
				MyAPIGateway.Entities.GetEntities(entities, x => x is IMyCubeGrid);
			});

			foreach (IMyEntity entity in entities)
			{
				if (entity.DisplayName.ToLower().Contains(displayName.ToLower()))
				{
					return entity;
				}
			}

			return null;
		}

		public static bool WaitForLoadingEntity(CubeGridEntity grid)
		{
			int count = 0;
			while (grid.IsLoading)
			{
				Thread.Sleep(100);
				count++;
				if (count > 10)
					return false;
			}

			return true;
		}

		public static HashSet<IMyEntity> ScanCleanup(ulong userId, string[] words)
		{
			Dictionary<string, string> options = new Dictionary<string,string>();

			bool requiresFunctional = true;
			bool requiresTerminal = true;
			bool requiresPower = true;
			bool hasDisplayName = false;
			bool ignoreOwnership = false;
			bool requiresOwner = false;
			bool debug = false;
			bool hasBlockSubType = false;
			bool hasBlockSubTypeLimits = false;

			options.Add("Requires Functional", "true");
			options.Add("Requires Terminal", "true");
			options.Add("Requires Valid Power", "true");
			options.Add("Matches Display Name Text", "false");
			options.Add("Ignore Ownership", "false");
			options.Add("Requires Ownership", "false");
			options.Add("Debug", "false");
			options.Add("Has Sub Block Type", "false");
			options.Add("Has Sub Block Type Limits", "false");

			string displayName = "";
			Dictionary<string, int> blockSubTypes = new Dictionary<string, int>();
			Dictionary<string, int> blockSubTypeLimits = new Dictionary<string, int>();

			if (words.Count() > 0)
			{
				if (words.FirstOrDefault(x => x.ToLower() == "debug") != null)
				{
					options["Debug"] = "true";
					debug = true;
				}

				if (words.SingleOrDefault(x => x.ToLower() == "ignoreownership") != null)
				{
					options["Ignore Ownership"] = "true";
					ignoreOwnership = true;
				}

				if (words.SingleOrDefault(x => x.ToLower() == "isowned") != null)
				{
					options["Requires Ownership"] = "true";
					requiresOwner = true;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "ignorefunctional") != null)
				{
					options["Requires Functional"] = "false";
					requiresFunctional = false;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "ignoreterminal") != null)
				{
					options["Requires Terminal"] = "false";
					requiresTerminal = false;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "ignorepower") != null)
				{
					options["Requires Valid Power"] = "ignore";
					requiresPower = false;
				}

				if (words.FirstOrDefault(x => x.ToLower().StartsWith("hasdisplayname:")) != null)
				{
					hasDisplayName = true;
					displayName = words.FirstOrDefault(x => x.ToLower().StartsWith("hasdisplayname:")).Split(new char[] { ':' })[1];
					options["Matches Display Name Text"] = "true:" + displayName;
				}

				if(words.FirstOrDefault(x => x.ToLower().StartsWith("hasblocksubtype:")) != null)
				{
					string[] parts = words.FirstOrDefault(x => x.ToLower().StartsWith("hasblocksubtype:")).Split(new char[] { ':' });
					hasBlockSubType = true;
					options["Has Sub Block Type"] = "true";

					if (parts.Length < 3)
					{
						blockSubTypes.Add(parts[1], 1);
						options.Add("Sub Block Type: " + parts[1], "1");
					}
					else
					{
						int count = 1;
						int.TryParse(parts[2], out count);
						blockSubTypes.Add(parts[1], count);
						options.Add("Sub Block Type: " + parts[1], count.ToString());
					}
				}

				if (words.FirstOrDefault(x => x.ToLower().StartsWith("limitblocksubtype:")) != null)
				{
					string[] parts = words.FirstOrDefault(x => x.ToLower().StartsWith("limitblocksubtype:")).Split(new char[] { ':' });
					hasBlockSubTypeLimits = true;
					options["Has Sub Block Type Limits"] = "true";

					if (parts.Length < 3)
					{
						blockSubTypes.Add(parts[1], 1);
						options.Add("Sub Block Type Limit: " + parts[1], "1");
					}
					else
					{
						int count = 1;
						int.TryParse(parts[2], out count);
						blockSubTypes.Add(parts[1], count);
						options.Add("Sub Block Type Limit: " + parts[1], count.ToString());
					}
				}
			}

			Communication.SendPrivateInformation(userId, string.Format("Scanning for ships with options: {0}", GetOptionsText(options)));

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			Wrapper.GameAction(() =>
			{
				MyAPIGateway.Entities.GetEntities(entities, x => x is IMyCubeGrid);
			});

			HashSet<IMyEntity> entitiesToConfirm = new HashSet<IMyEntity>();
			HashSet<IMyEntity> entitiesUnconnected = new HashSet<IMyEntity>();
			HashSet<IMyEntity> entitiesFound = new HashSet<IMyEntity>();
			foreach (IMyEntity entity in entities)
			{
				if (!(entity is IMyCubeGrid))
					continue;

				IMyCubeGrid grid = (IMyCubeGrid)entity;
				MyObjectBuilder_CubeGrid gridBuilder = null;
				try
				{
					 gridBuilder = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
				}
				catch				
				{
					continue;
				}

				//CubeGridEntity gridEntity = (CubeGridEntity)GameEntityManager.GetEntity(grid.EntityId);

				if (PluginSettings.Instance.LoginEntityWhitelist.Contains(entity.EntityId.ToString()) || PluginSettings.Instance.LoginEntityWhitelist.Contains(entity.DisplayName))
					continue;

				if (hasDisplayName && displayName != "")
				{
					if (entity.DisplayName.Contains(displayName))
						entitiesToConfirm.Add(entity);
				}
				else if (ignoreOwnership)
				{
					entitiesToConfirm.Add(entity);
				}
				else if (requiresOwner && HasOwner(gridBuilder))
				{
					entitiesToConfirm.Add(entity);
				}
				else if (!requiresOwner && !HasOwner(gridBuilder))
				{
					entitiesToConfirm.Add(entity);
				}
			}

			Dictionary<string, int> subTypeDict = new Dictionary<string, int>();
			CubeGrids.GetBlocksUnconnected(entitiesUnconnected, entitiesToConfirm);
			foreach (IMyEntity entity in entitiesUnconnected)
			{
				subTypeDict.Clear();
				MyObjectBuilder_CubeGrid grid = (MyObjectBuilder_CubeGrid)entity.GetObjectBuilder();
				bool found = false;
				foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
				{
					if (requiresFunctional)
					{
						if (block is MyObjectBuilder_FunctionalBlock)
						{
							if (debug && !found)
								Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has a functional block.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							found = true;
						}
					}

					if (requiresTerminal)
					{
						if (block is MyObjectBuilder_TerminalBlock)
						{
							if (debug && !found)
								Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has a terminal block.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							found = true;
						}
					}


					if (requiresPower)
					{
						if (DoesBlockSupplyPower(block))
						{
							if (debug && !found)
								Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has power.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							found = true;
						}
					}

					if (hasBlockSubType || hasBlockSubTypeLimits)
					{
						string subTypeName = block.GetId().SubtypeName;
						if (subTypeDict.ContainsKey(subTypeName))
							subTypeDict[subTypeName] = subTypeDict[subTypeName] + 1;
						else
							subTypeDict.Add(subTypeName, 1);
					}
				}

				if(hasBlockSubType)
				{
					foreach(KeyValuePair<string, int> p in subTypeDict)
					{
						foreach (KeyValuePair<string, int> s in blockSubTypes)
						{
							if(p.Key.ToLower().Contains(s.Key.ToLower()))
							{
								if(p.Value >= s.Value)
								{
									if (debug)
										Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which contains at least {4} of block type {3} ({5}).  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count, p.Key, s.Value, p.Value));

									found = true;
									break;
								}
							}
						}
					}
				}

				if (hasBlockSubTypeLimits && found)
				{
					foreach (KeyValuePair<string, int> p in subTypeDict)
					{
						foreach (KeyValuePair<string, int> s in blockSubTypes)
						{
							if (p.Key.ToLower().Contains(s.Key.ToLower()))
							{
								if (p.Value > s.Value)
								{
									if (found)
										found = false;

									Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which is over limit of block type {3} at {4}.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count, s.Key, p.Value));
									break;
								}
							}
						}
					}
				}

				if (!found)
					entitiesFound.Add(entity);
			}

			foreach (IMyEntity entity in entitiesFound)
			{
				Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has unconnected and has parameters specified.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));
			}

			Communication.SendPrivateInformation(userId, string.Format("Found {0} grids considered to be trash", entitiesFound.Count));
			return entitiesFound;
		}

		public static HashSet<IMyEntity> ScanGrids(ulong userId, string[] words)
		{
			Dictionary<string, string> options = new Dictionary<string, string>();

			// 0 - ignore 1 - no 2 - yes
			// functional
			// terminal
			// ownership
			// power
			int functional = 0;
			int terminal = 0;
			int power = 0;
			int owner = 0;

			// debug
			// hasdisplayname
			// blocksubtype
			// blocksubtypelimit
			bool hasDisplayName = false;
			bool hasBlockSubType = false;
			bool hasBlockSubTypeLimits = false;
			bool debug = false;

			string displayName = "";
			Dictionary<string, int> blockSubTypes = new Dictionary<string, int>();
			Dictionary<string, int> blockSubTypeLimits = new Dictionary<string, int>();
			if (words.Count() > 0)
			{
				if (words.FirstOrDefault(x => x.ToLower() == "debug") != null)
				{
					options.Add("Debug", "true");
					debug = true;
				}

				if (words.SingleOrDefault(x => x.ToLower() == "ownership") != null)
				{
					options.Add("Ownership", "true");
					owner = 2;
				}

				if (words.SingleOrDefault(x => x.ToLower() == "noownership") != null)
				{
					options.Add("Ownership", "false");
					owner = 1;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "functional") != null)
				{
					options.Add("Functional", "true");
					functional = 2;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "nofunctional") != null)
				{
					options.Add("Functional", "false");
					functional = 1;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "terminal") != null)
				{
					options.Add("Terminal", "true");
					terminal = 2;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "noterminal") != null)
				{
					options.Add("Terminal", "false");
					terminal = 1;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "power") != null)
				{
					options.Add("Has Power", "true");
					power = 2;
				}

				if (words.FirstOrDefault(x => x.ToLower() == "nopower") != null)
				{
					options.Add("Has Power", "false");
					power = 1;
				}

				if (words.FirstOrDefault(x => x.ToLower().StartsWith("hasdisplayname:")) != null)
				{
					hasDisplayName = true;
					displayName = words.FirstOrDefault(x => x.ToLower().StartsWith("hasdisplayname:")).Split(new char[] { ':' })[1];
					options.Add("Matches Display Name Text", "true:" + displayName);
				}

				if (words.FirstOrDefault(x => x.ToLower().StartsWith("hasblocksubtype:")) != null)
				{
					string[] parts = words.FirstOrDefault(x => x.ToLower().StartsWith("hasblocksubtype:")).Split(new char[] { ':' });
					hasBlockSubType = true;
					options.Add("Has Sub Block Type", "true");

					if (parts.Length < 3)
					{
						blockSubTypes.Add(parts[1], 1);
						options.Add("Sub Block Type: " + parts[1], "1");
					}
					else
					{
						int count = 1;
						int.TryParse(parts[2], out count);
						blockSubTypes.Add(parts[1], count);
						options.Add("Sub Block Type: " + parts[1], count.ToString());
					}
				}

				if (words.FirstOrDefault(x => x.ToLower().StartsWith("limitblocksubtype:")) != null)
				{
					string[] parts = words.FirstOrDefault(x => x.ToLower().StartsWith("limitblocksubtype:")).Split(new char[] { ':' });
					hasBlockSubTypeLimits = true;
					options.Add("Has Sub Block Type Limits", "true");

					if (parts.Length < 3)
					{
						blockSubTypes.Add(parts[1], 1);
						options.Add("Sub Block Type Limit: " + parts[1], "1");
					}
					else
					{
						int count = 1;
						int.TryParse(parts[2], out count);
						blockSubTypes.Add(parts[1], count);
						options.Add("Sub Block Type Limit: " + parts[1], count.ToString());
					}
				}
			}

			Communication.SendPrivateInformation(userId, string.Format("Scanning for ships with options: {0}", GetOptionsText(options)));

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
			Wrapper.GameAction(() =>
			{
				MyAPIGateway.Entities.GetEntities(entities, x => x is IMyCubeGrid);
			});

			HashSet<IMyEntity> entitiesToConfirm = new HashSet<IMyEntity>();
			HashSet<IMyEntity> entitiesUnconnected = new HashSet<IMyEntity>();
			HashSet<IMyEntity> entitiesFound = new HashSet<IMyEntity>();
			foreach (IMyEntity entity in entities)
			{
				if (!(entity is IMyCubeGrid))
					continue;

				IMyCubeGrid grid = (IMyCubeGrid)entity;
				MyObjectBuilder_CubeGrid gridBuilder = null;
				try
				{
					gridBuilder = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
				}
				catch
				{
					continue;
				}

				//CubeGridEntity gridEntity = (CubeGridEntity)GameEntityManager.GetEntity(grid.EntityId);

				if (PluginSettings.Instance.LoginEntityWhitelist.Contains(entity.EntityId.ToString()) || PluginSettings.Instance.LoginEntityWhitelist.Contains(entity.DisplayName))
					continue;

				if (hasDisplayName && displayName != "")
				{
					if (entity.DisplayName.Contains(displayName))
						entitiesToConfirm.Add(entity);
				}
				else if (owner == 0)
				{
					entitiesToConfirm.Add(entity);
				}
				else if (owner == 2 && HasOwner(gridBuilder))
				{
					entitiesToConfirm.Add(entity);
				}
				else if (owner == 1 && !HasOwner(gridBuilder))
				{
					entitiesToConfirm.Add(entity);
				}
			}

			Dictionary<string, int> subTypeDict = new Dictionary<string, int>();
			List<string> checkList = new List<string>();
			CubeGrids.GetBlocksUnconnected(entitiesUnconnected, entitiesToConfirm);
			foreach (IMyEntity entity in entitiesUnconnected)
			{
				subTypeDict.Clear();
				checkList.Clear();
				MyObjectBuilder_CubeGrid grid = (MyObjectBuilder_CubeGrid)entity.GetObjectBuilder();
				bool found = true;
				foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
				{
					if (functional != 0)
					{
						if (block is MyObjectBuilder_FunctionalBlock)
						{
//							if (debug && !found)
//								Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has a functional block.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							if (!checkList.Contains("functional"))
								checkList.Add("functional");
						}
					}

					if (terminal != 0)
					{
						if (block is MyObjectBuilder_TerminalBlock)
						{
							//if (debug && !found)
							//	Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has a terminal block.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							if (!checkList.Contains("terminal"))
								checkList.Add("terminal");
						}
					}


					if (power != 0)
					{
						if (DoesBlockSupplyPower(block))
						{
							//if (debug && !found)
							//	Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which has power.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

							if (!checkList.Contains("power"))
								checkList.Add("power");
						}
					}

					if (hasBlockSubType || hasBlockSubTypeLimits)
					{
						string subTypeName = block.GetId().SubtypeName;
						if (subTypeDict.ContainsKey(subTypeName))
							subTypeDict[subTypeName] = subTypeDict[subTypeName] + 1;
						else
							subTypeDict.Add(subTypeName, 1);
					}
				}

				if (functional != 0)
				{
					if (!checkList.Contains("functional") && functional == 2)
						found = false;

					if (checkList.Contains("functional") && functional == 1)
						found = false;
				}

				if(terminal != 0)
				{
					if (!checkList.Contains("terminal") && terminal == 2)
						found = false;

					if (checkList.Contains("terminal") && terminal == 1)
						found = false;
				}

				if (power != 0)
				{
					if (!checkList.Contains("power") && power == 2)
						found = false;

					if (checkList.Contains("power") && power == 1)
						found = false;
				}

				if (hasBlockSubType)
				{
					bool hasType = false;
					foreach (KeyValuePair<string, int> p in subTypeDict)
					{
						foreach (KeyValuePair<string, int> s in blockSubTypes)
						{
							if (p.Key.ToLower().Contains(s.Key.ToLower()))
							{
								if (p.Value >= s.Value)
								{
									if (debug)
										Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which contains at least {4} of block type {3} ({5}).  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count, p.Key, s.Value, p.Value));

									hasType = true;
									break;
								}
							}
						}
					}

					if (!hasType)
					{
						if (debug)
							Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which does not contain block type.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));

						found = false;
					}
				}

				if (hasBlockSubTypeLimits && found)
				{
					foreach (KeyValuePair<string, int> p in subTypeDict)
					{
						foreach (KeyValuePair<string, int> s in blockSubTypes)
						{
							if (p.Key.ToLower().Contains(s.Key.ToLower()))
							{
								if (p.Value > s.Value)
								{
									if (!found)
										found = true;

									Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which is over limit of block type {3} at {4}.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count, s.Key, p.Value));
									break;
								}
							}
						}
					}
				}

				if (found)
					entitiesFound.Add(entity);
			}

			foreach (IMyEntity entity in entitiesFound)
			{
				Communication.SendPrivateInformation(userId, string.Format("Found grid '{0}' ({1}) which is unconnected with specified parameters.  BlockCount={2}", entity.DisplayName, entity.EntityId, ((MyObjectBuilder_CubeGrid)entity.GetObjectBuilder()).CubeBlocks.Count));
			}

			Communication.SendPrivateInformation(userId, string.Format("Found {0} grids", entitiesFound.Count));
			return entitiesFound;
		}

		public static bool GetOwner(MyObjectBuilder_CubeGrid grid, out long ownerId)
		{
			ownerId = 0;
			foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
			{
				if (!(block is MyObjectBuilder_TerminalBlock))
					continue;

				MyObjectBuilder_TerminalBlock functional = (MyObjectBuilder_TerminalBlock)block;
				if (functional.Owner != 0)
				{
					ownerId = functional.Owner;
					return true;
				}
			}

			return false;
		}

		public static bool HasOwner(MyObjectBuilder_CubeGrid grid)
		{
			foreach (MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
			{
				if (!(block is MyObjectBuilder_TerminalBlock))
					continue;

				MyObjectBuilder_TerminalBlock functional = (MyObjectBuilder_TerminalBlock)block;
				if (functional.Owner != 0)
					return true;
			}

			return false;
		}


		private static string GetOptionsText(Dictionary<string, string> options)
		{
			string result = "";

			foreach(KeyValuePair<string, string> p in options)
			{
				if (result != "")
					result += ", ";

				result += p.Key + "=" + p.Value;
			}

			return result;
		}

		public static bool DoesBlockSupplyPower(MyObjectBuilder_CubeBlock block)
		{
			if (block is MyObjectBuilder_BatteryBlock)
			{
				MyObjectBuilder_BatteryBlock battery = (MyObjectBuilder_BatteryBlock)block;
				if (battery.CurrentStoredPower > 0f)
					return true;
			}

			if (block is MyObjectBuilder_Reactor)
			{
				MyObjectBuilder_Reactor reactor = (MyObjectBuilder_Reactor)block;
				if (reactor.Inventory.Items.Count > 0)
					return true;
			}

			if (block is MyObjectBuilder_SolarPanel)
			{
				return true;
			}

			return false;
		}

		public static bool DoesGridHavePowerSupply(MyObjectBuilder_CubeGrid grid)
		{
			foreach(MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
			{
				if (DoesBlockSupplyPower(block))
					return true;
			}

			return false;
		}

		public static bool DoesGridHaveFourBeacons(MyObjectBuilder_CubeGrid grid)
		{
			int count = 0;
			foreach(MyObjectBuilder_CubeBlock block in grid.CubeBlocks)
			{
				if (block is MyObjectBuilder_Beacon)
					count++;

				if (count >= 4)
					return true;
			}

			return false;
		}

		public static MyObjectBuilder_CubeGrid SafeGetObjectBuilder(IMyCubeGrid grid)
		{
			MyObjectBuilder_CubeGrid gridBuilder = null;
			try
			{
				gridBuilder = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder();
			}
			catch
			{
			}

			return gridBuilder;
		}


        /// <summary>
        /// This only returns one grid per connected grid.  So if a grid has a connector and 4 pistons, it will count as 1 grid, not 5.
        /// </summary>
        /// <param name="grids"></param>
        /// <param name="collect"></param>
        public static void GetConnectedGrids(HashSet<IMyEntity> grids, Func<IMyEntity, bool> collect = null)
        {
            List<IMySlimBlock> currentBlocks = new List<IMySlimBlock>();
            List<IMyCubeGrid> connectedGrids = new List<IMyCubeGrid>();
            HashSet<IMyEntity> gridsProcessed = new HashSet<IMyEntity>();
            HashSet<IMyEntity> entities = new HashSet<IMyEntity>();

            MyAPIGateway.Entities.GetEntities(entities, collect);
            foreach (IMyEntity entity in entities)
            {
                if (!(entity is IMyCubeGrid))
                    continue;

                IMyCubeGrid grid = (IMyCubeGrid)entity;
                if (gridsProcessed.Contains(grid))
                    continue;

                grids.Add(grid);
                GetGridBlocks(grid, currentBlocks);
                foreach (IMyCubeGrid connectedGrid in GetConnectedGridList(gridsProcessed, currentBlocks))
                {
                    gridsProcessed.Add(connectedGrid);
                }
            }
        }
        
        /// <summary>
        /// Gets all the blocks from all valid connected grids.  So a grid connected to another grid that also has a few pistons with blocks on it will return
        /// all the blocks for the connected grids as well as all the blocks for any connected pistons.  (ug)
        /// </summary>
        /// <param name="gridsProcessed"></param>
        /// <param name="grid"></param>
        /// <param name="allBlocks"></param>
        /// <param name="collect"></param>
        public static void GetAllConnectedBlocks(HashSet<IMyEntity> gridsProcessed, IMyCubeGrid grid, List<IMySlimBlock> allBlocks, Func<IMySlimBlock, bool> collect = null)
        {
            List<IMySlimBlock> currentBlocks = new List<IMySlimBlock>();
            List<IMyCubeGrid> connectedGrids = new List<IMyCubeGrid>();

            connectedGrids.Add(grid);
            while(connectedGrids.Count > 0)
            {
                IMyCubeGrid currentGrid = connectedGrids.First();                
                connectedGrids.Remove(currentGrid);
                if (gridsProcessed.Contains(currentGrid))
                    continue;

                gridsProcessed.Add(currentGrid);

                GetGridBlocks(currentGrid, currentBlocks);
                foreach (IMyCubeGrid connectedGrid in GetConnectedGridList(gridsProcessed, currentBlocks))
                {
                    connectedGrids.Add(connectedGrid);
                }

                if (collect != null)
                {
                    foreach (IMySlimBlock slimBlock in currentBlocks.FindAll(s => collect(s)))
                        allBlocks.Add(slimBlock);
                }
                else
                {
                    foreach (IMySlimBlock slimBlock in currentBlocks)
                        allBlocks.Add(slimBlock);
                }
            }
        }

        private static List<IMyCubeGrid> GetConnectedGridList(HashSet<IMyEntity> checkedGrids, List<IMySlimBlock> blocks)
        {
            List<IMyCubeGrid> connectedGrids = new List<IMyCubeGrid>();
            foreach (IMySlimBlock slimBlock in blocks)
            {
                if (slimBlock.FatBlock != null && slimBlock.FatBlock is IMyCubeBlock)
                {
                    IMyCubeBlock cubeBlock = (IMyCubeBlock)slimBlock.FatBlock;

                    // Check for Piston
                    if (cubeBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_PistonBase))
                    {
                        MyObjectBuilder_PistonBase pistonBase = (MyObjectBuilder_PistonBase)cubeBlock.GetObjectBuilderCubeBlock();
                        IMyEntity entity = null;
                        if (MyAPIGateway.Entities.TryGetEntityById(pistonBase.TopBlockId, out entity))
                        {
                            IMyCubeGrid parent = (IMyCubeGrid)entity.Parent;
                            if(!checkedGrids.Contains(parent))
                                connectedGrids.Add(parent);
                        }
                    }
					else if (cubeBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_ExtendedPistonBase))
					{
						MyObjectBuilder_PistonBase pistonBase = (MyObjectBuilder_PistonBase)cubeBlock.GetObjectBuilderCubeBlock();
						IMyEntity entity = null;
						if (MyAPIGateway.Entities.TryGetEntityById(pistonBase.TopBlockId, out entity))
						{
							IMyCubeGrid parent = (IMyCubeGrid)entity.Parent;
							if (!checkedGrids.Contains(parent))
								connectedGrids.Add(parent);
						}
					}
					// Connector    
                    else if (cubeBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_ShipConnector))
                    {
                        MyObjectBuilder_ShipConnector connector = (MyObjectBuilder_ShipConnector)cubeBlock.GetObjectBuilderCubeBlock();
                        IMyEntity entity = null;
                        if (MyAPIGateway.Entities.TryGetEntityById(connector.ConnectedEntityId, out entity))
                        {
                            IMyCubeGrid parent = (IMyCubeGrid)entity.Parent;
                            if (!checkedGrids.Contains(parent))
                                connectedGrids.Add(parent);
                        }
                    }
					else if (cubeBlock.BlockDefinition.TypeId == typeof(MyObjectBuilder_MotorAdvancedStator))
					{
						MyObjectBuilder_MotorAdvancedStator stator = (MyObjectBuilder_MotorAdvancedStator)cubeBlock.GetObjectBuilderCubeBlock();
						IMyEntity connectedEntity = null;
						if (MyAPIGateway.Entities.TryGetEntityById(stator.RotorEntityId, out connectedEntity))
						{
							IMyCubeGrid parent = (IMyCubeGrid)connectedEntity.Parent;
							if (!checkedGrids.Contains(parent))
								connectedGrids.Add(parent);
						}
					}
                }
            }

            return connectedGrids;
        }

        private static void GetGridBlocks(IMyCubeGrid grid, List<IMySlimBlock> blockList, Func<IMySlimBlock, bool> collect = null)
        {
            blockList.Clear();
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, collect);
            foreach (IMySlimBlock block in blocks)
                blockList.Add(block);
        }    
	}
}
