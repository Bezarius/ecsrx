﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EcsRx.Entities;
using EcsRx.Events;
using EcsRx.Extensions;
using EcsRx.Groups;
using EcsRx.Pools;
using EcsRx.Systems.Executor.Handlers;
using UniRx;

namespace EcsRx.Systems.Executor
{
    public sealed class SystemExecutor : ISystemExecutor, IDisposable
    {
        private readonly IList<ISystem> _systems;
        private readonly IList<IDisposable> _eventSubscriptions;
        private readonly Dictionary<ISystem, IList<SubscriptionToken>> _systemSubscriptions;

        public IEventSystem EventSystem { get; private set; }
        public IPoolManager PoolManager { get; private set; }
        public IEnumerable<ISystem> Systems { get { return _systems; } }

        public IReactToEntitySystemHandler ReactToEntitySystemHandler { get; private set; }
        public IReactToGroupSystemHandler ReactToGroupSystemHandler { get; private set; }
        public ISetupSystemHandler SetupSystemHandler { get; private set; }
        public IReactToDataSystemHandler ReactToDataSystemHandler { get; private set; }
        public IManualSystemHandler ManualSystemHandler { get; private set; }

        public SystemExecutor(IPoolManager poolManager, IEventSystem eventSystem,
            IReactToEntitySystemHandler reactToEntitySystemHandler, IReactToGroupSystemHandler reactToGroupSystemHandler,
            ISetupSystemHandler setupSystemHandler, IReactToDataSystemHandler reactToDataSystemHandler,
            IManualSystemHandler manualSystemHandler)
        {
            PoolManager = poolManager;
            EventSystem = eventSystem;
            ReactToEntitySystemHandler = reactToEntitySystemHandler;
            ReactToGroupSystemHandler = reactToGroupSystemHandler;
            SetupSystemHandler = setupSystemHandler;
            ReactToDataSystemHandler = reactToDataSystemHandler;
            ManualSystemHandler = manualSystemHandler;

            var addEntitySubscription = EventSystem.Receive<EntityAddedEvent>().Subscribe(OnEntityAddedToPool);
            var removeEntitySubscription = EventSystem.Receive<EntityRemovedEvent>().Subscribe(OnEntityRemovedFromPool);
            var addComponentSubscription = EventSystem.Receive<ComponentAddedEvent>().Subscribe(OnEntityComponentAdded);
            var addComponentsSubscription =
                EventSystem.Receive<ComponentsAddedEvent>().Subscribe(OnEntityComponentsAdded);
            var removeComponentSubscription = EventSystem.Receive<ComponentRemovedEvent>().Subscribe(OnEntityComponentRemoved);

            _systems = new List<ISystem>();
            _systemSubscriptions = new Dictionary<ISystem, IList<SubscriptionToken>>();
            _eventSubscriptions = new List<IDisposable>
            {
                addEntitySubscription,
                removeEntitySubscription,
                addComponentSubscription,
                removeComponentSubscription,
                addComponentsSubscription
            };

        }

        public void OnEntityComponentRemoved(ComponentRemovedEvent args)
        {
            var type = args.Component.GetType();

            // get affected systems for remove subscribes
            var componentList = args.Entity.Components.Select(x => x.GetType()).ToList();
            componentList.Add(type);
            var effectedSystems = _systems
                .Where(x => x.TargetGroup.TargettedComponents.Contains(type))
                .Where(x => x.TargetGroup.TargettedComponents
                .All(componentList.Contains)).ToList();

            foreach (var effectedSystem in effectedSystems)
            {
                if (effectedSystem is ITeardownSystem)
                { (effectedSystem as ITeardownSystem).Teardown(args.Entity); }

                var subscriptionTokens = _systemSubscriptions[effectedSystem]
                    .Where(x => x.AssociatedObject == args.Entity)
                    .ToList();

                _systemSubscriptions[effectedSystem].RemoveAll(subscriptionTokens);
                subscriptionTokens.DisposeAll();
            }
        }

        public void OnEntityComponentAdded(ComponentAddedEvent args)
        {
            var applicableSystems = _systems.GetApplicableSystems(args.Entity);
            var effectedSystems = applicableSystems.Where(x => x.TargetGroup.TargettedComponents.Contains(args.Component.GetType()));

            ApplyEntityToSystems(effectedSystems, args.Entity);
        }

        private readonly Dictionary<SystemGroupKey, List<ISystem>> _systemGroups =  new Dictionary<SystemGroupKey, List<ISystem>>();

        public void OnEntityComponentsAdded(ComponentsAddedEvent args)
        {
            var types = args.Components.Select(x => x.GetType()).ToArray();
            var group = new SystemGroupKey(types);
            if (!_systemGroups.ContainsKey(group))
            {
                
            }
        }

        public void OnEntityAddedToPool(EntityAddedEvent args)
        {
            if (!args.Entity.Components.Any()) { return; }

            var applicableSystems = _systems.GetApplicableSystems(args.Entity);
            ApplyEntityToSystems(applicableSystems, args.Entity);
        }

        public void OnEntityRemovedFromPool(EntityRemovedEvent args)
        {
            var applicableSystems = _systems.GetApplicableSystems(args.Entity);
            applicableSystems.ForEachRun(x => RemoveSubscription(x, args.Entity));
        }

        private void ApplyEntityToSystems(IEnumerable<ISystem> systems, IEntity entity)
        {
            systems.OfType<ISetupSystem>()
                .OrderByPriority()
                .ForEachRun(x =>
                {
                    var possibleSubscription = SetupSystemHandler.ProcessEntity(x, entity);
                    if (possibleSubscription != null)
                    { _systemSubscriptions[x].Add(possibleSubscription); }
                });

            systems.OfType<IReactToEntitySystem>()
                .OrderByPriority()
                .ForEachRun(x =>
                {
                    var subscription = ReactToEntitySystemHandler.ProcessEntity(x, entity);
                    _systemSubscriptions[x].Add(subscription);
                });

            systems.Where(x => x.IsReactiveDataSystem())
                .OrderByPriority()
                .ForEachRun(x =>
                {
                    var subscription = ReactToDataSystemHandler.ProcessEntityWithoutType(x, entity);
                    _systemSubscriptions[x].Add(subscription);
                });
        }

        public void RemoveSubscription(ISystem system, IEntity entity)
        {
            var subscriptionList = _systemSubscriptions[system];
            var subscriptionTokens = subscriptionList.GetTokensFor(entity).ToArray();

            if (!subscriptionTokens.Any()) { return; }

            subscriptionTokens.ForEachRun(x => subscriptionList.Remove(x));
            subscriptionTokens.DisposeAll();
        }

        public void RemoveSystem(ISystem system)
        {
            _systems.Remove(system);

            if (system is IManualSystem)
            { ManualSystemHandler.Stop(system as IManualSystem); }

            if (_systemSubscriptions.ContainsKey(system))
            {
                _systemSubscriptions[system].DisposeAll();
                _systemSubscriptions.Remove(system);
            }
        }

        public void AddSystem(ISystem system)
        {
            _systems.Add(system);
            var subscriptionList = new List<SubscriptionToken>();

            if (system is ISetupSystem)
            {
                var subscriptions = SetupSystemHandler.Setup(system as ISetupSystem);
                subscriptionList.AddRange(subscriptions);
            }

            if (system is IReactToGroupSystem)
            {
                var subscription = ReactToGroupSystemHandler.Setup(system as IReactToGroupSystem);
                subscriptionList.Add(subscription);
            }

            if (system is IReactToEntitySystem)
            {
                var subscriptions = ReactToEntitySystemHandler.Setup(system as IReactToEntitySystem);
                subscriptionList.AddRange(subscriptions);
            }

            if (system.IsReactiveDataSystem())
            {
                var subscriptions = ReactToDataSystemHandler.SetupWithoutType(system);
                subscriptionList.AddRange(subscriptions);
            }

            if (system is IManualSystem)
            { ManualSystemHandler.Start(system as IManualSystem); }

            _systemSubscriptions.Add(system, subscriptionList);
        }

        public int GetSubscriptionCountForSystem(ISystem system)
        {
            if (!_systemSubscriptions.ContainsKey(system)) { return 0; }
            return _systemSubscriptions[system].Count;
        }

        public int GetTotalSubscriptions()
        { return _systemSubscriptions.Values.Sum(x => x.Count); }

        public void Dispose()
        {
            _systemSubscriptions.ForEachRun(x => x.Value.DisposeAll());
            _eventSubscriptions.DisposeAll();
        }
    }
}