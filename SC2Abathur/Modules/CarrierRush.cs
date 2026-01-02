using System.Collections.Generic;
using System.Linq;
using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using NydusNetwork.API.Protocol;

namespace SC2Abathur.Modules.Examples
{
    public class CarrierRush : IReplaceableModule
    {
        private IEnumerable<IColony> _eStarts;
        private readonly IIntelManager _intelManager;
        private readonly ICombatManager _combatManager;
        private readonly IProductionManager _productionManager;
        private readonly ISquadRepository _squadRep;

        private Squad _armada;
        private bool _startCalled;
        private bool _attackMode;

        // Словник цін
        private readonly Dictionary<uint, (int Min, int Gas)> _unitCosts = new Dictionary<uint, (int, int)>
        {
            { BlizzardConstants.Unit.Nexus, (400, 0) },
            { BlizzardConstants.Unit.Pylon, (100, 0) },
            { BlizzardConstants.Unit.Assimilator, (75, 0) },
            { BlizzardConstants.Unit.Gateway, (150, 0) },
            { BlizzardConstants.Unit.CyberneticsCore, (150, 0) },
            { BlizzardConstants.Unit.Stalker, (125, 50) },
            { BlizzardConstants.Unit.Stargate, (150, 150) },
            { BlizzardConstants.Unit.FleetBeacon, (300, 200) },
            { BlizzardConstants.Unit.Carrier, (350, 250) }
        };

        public CarrierRush(
            IIntelManager intelManager,
            ICombatManager combatManager,
            IProductionManager productionManager,
            ISquadRepository squadRepo)
        {
            _intelManager = intelManager;
            _combatManager = combatManager;
            _productionManager = productionManager;
            _squadRep = squadRepo;
        }

        public void Initialize() { }

        public void OnStart()
        {
            if (_startCalled) return;
            _eStarts = _intelManager.Colonies.Where(c => c.IsStartingLocation);

            //_productionManager.QueueUnit(BlizzardConstants.Unit.Pylon);

            _intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, UnitCreationHandler);
            _armada = _squadRep.Create("GoldenArmada");
            _startCalled = true;
        }

        public void OnStep()
        {
            var myStructures = _intelManager.StructuresSelf();
            var prodQueue = _intelManager.ProductionQueue;

            bool hasGateway = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.Gateway) || prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.Gateway);
            bool hasCyberCore = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.CyberneticsCore) || prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.CyberneticsCore);
            bool hasStargate = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.Stargate) || prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.Stargate);
            bool hasBeacon = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.FleetBeacon) || prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.FleetBeacon);

            int gasCount = myStructures.Count(u => u.UnitType == BlizzardConstants.Unit.Assimilator) + prodQueue.Count(q => q.UnitId == BlizzardConstants.Unit.Assimilator);

            bool isGatewayReady = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.Gateway && u.BuildProgress == 1);
            bool isCyberCoreReady = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.CyberneticsCore && u.BuildProgress == 1);
            bool isStargateReady = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.Stargate && u.BuildProgress == 1);
            bool isBeaconReady = myStructures.Any(u => u.UnitType == BlizzardConstants.Unit.FleetBeacon && u.BuildProgress == 1);

            if (_intelManager.UnitsSelf().Count(u => u.UnitType == BlizzardConstants.Unit.Probe) < 12)
            {
                if (CanAfford(BlizzardConstants.Unit.Probe) && !prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.Probe))
                    _productionManager.QueueUnit(BlizzardConstants.Unit.Probe);
            }

            if (_intelManager.Common.FoodUsed > _intelManager.Common.FoodCap - 6 && _intelManager.Common.FoodCap < 200)
            {
                if (CanAfford(BlizzardConstants.Unit.Pylon) && !prodQueue.Any(q => q.UnitId == BlizzardConstants.Unit.Pylon))
                    _productionManager.QueueUnit(BlizzardConstants.Unit.Pylon);
            }

            if (!hasGateway && CanAfford(BlizzardConstants.Unit.Gateway))
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.Gateway);
            }

            else if (hasGateway && gasCount < 1 && CanAfford(BlizzardConstants.Unit.Assimilator))
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.Assimilator);
            }

            else if (isGatewayReady && !hasCyberCore && CanAfford(BlizzardConstants.Unit.CyberneticsCore))
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.CyberneticsCore);
            }

            else if (hasCyberCore && gasCount < 2 && CanAfford(BlizzardConstants.Unit.Assimilator))
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.Assimilator);
            }

            else if (isCyberCoreReady && !hasStargate)
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.Stargate);
            }

            else if (isStargateReady && !hasBeacon)
            {
                _productionManager.QueueUnit(BlizzardConstants.Unit.FleetBeacon);
            }

            else if (isBeaconReady)
            {
                if (CanAfford(BlizzardConstants.Unit.Carrier))
                    _productionManager.QueueUnit(BlizzardConstants.Unit.Carrier);
            }

            ManageAttack(_intelManager.UnitsSelf());
        }

        private bool CanAfford(uint unitType)
        {
            if (!_unitCosts.ContainsKey(unitType)) return true;
            var cost = _unitCosts[unitType];
            return _intelManager.Common.Minerals >= cost.Min && _intelManager.Common.Vespene >= cost.Gas;
        }

        private void ManageAttack(IEnumerable<IUnit> myUnits)
        {
            var carrierCount = myUnits.Count(u => u.UnitType == BlizzardConstants.Unit.Carrier);
            if (carrierCount >= 2 && !_attackMode)
            {
                _attackMode = true;
                foreach (var colony in _eStarts)
                    _combatManager.AttackMove(_armada, colony.Point, true);
            }

            if (_attackMode && _intelManager.GameLoop % 200 == 0)
            {
                var target = _intelManager.StructuresEnemyVisible.FirstOrDefault();
                if (target != null) _combatManager.AttackMove(_armada, target.Point);
                else foreach (var c in _eStarts) _combatManager.AttackMove(_armada, c.Point, true);
            }
        }

        public void UnitCreationHandler(IUnit u)
        {
            if (u.UnitType == BlizzardConstants.Unit.Carrier || u.UnitType == BlizzardConstants.Unit.VoidRay || u.UnitType == BlizzardConstants.Unit.Stalker)
            {
                _armada.AddUnit(u);
                if (_attackMode) ManageAttack(_intelManager.UnitsSelf());
            }
        }

        public void OnGameEnded() { _startCalled = false; _attackMode = false; }
        public void OnRestart() => OnGameEnded();
        public void OnAdded() => OnStart();
        public void OnRemoved() => OnGameEnded();
        public void QueueOpeningBuild() { }
    }
}