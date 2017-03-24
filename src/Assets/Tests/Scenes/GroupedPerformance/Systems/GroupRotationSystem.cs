﻿using Assets.Tests.Scenes.GroupedPerformance.Components;
using Reactor.Entities;
using Reactor.Groups;
using Reactor.Systems;
using Reactor.Unity.Components;
using UniRx;
using UnityEngine;

namespace Assets.Tests.Scenes.GroupedPerformance.Systems
{
    public class GroupRotationSystem : IReactToGroupSystem
    {
        public IGroup TargetGroup { get { return new Group(typeof(ViewComponent), typeof(RotationComponent));} }

        public IObservable<IGroupAccessor> ReactToGroup(IGroupAccessor @group)
        {
            return Observable.EveryUpdate().Select(x => @group);
        }

        public void Execute(IEntity entity)
        {
            var rotationComponent = entity.GetComponent<RotationComponent>();
            var viewComponent = entity.GetComponent<ViewComponent>();

            var rotation = rotationComponent.RotationSpeed*Time.deltaTime;
            viewComponent.View.transform.Rotate(0, rotation, 0);
        }
    }
}