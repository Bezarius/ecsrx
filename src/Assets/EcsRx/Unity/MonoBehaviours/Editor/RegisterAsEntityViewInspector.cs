﻿using Assets.EcsRx.Unity.Extensions;
using EcsRx.Json;
using EcsRx.Unity.Components;
using EcsRx.Unity.Helpers.Extensions;
using EcsRx.Unity.Helpers.UIAspects;
using EcsRx.Unity.MonoBehaviours;
using UniRx;

namespace EcsRx.Unity.Helpers
{
    using UnityEngine;
    using UnityEditor;
    using System.Collections.Generic;
    using EcsRx.Components;
    using System;
    using System.Linq;

    [Serializable]
    [CustomEditor(typeof(RegisterAsEntity))]
    public partial class RegisterAsEntityViewInspector : Editor
    {
        private RegisterAsEntity _registerAsEntity;

        private readonly IEnumerable<Type> allComponentTypes = AppDomain.CurrentDomain.GetAssemblies()
                                .SelectMany(s => s.GetTypes())
                                .Where(p => typeof(IComponent).IsAssignableFrom(p) && p.IsClass && !typeof(ViewComponent).IsAssignableFrom(p));

        private bool showComponents;

        private void PoolSection()
        {
            this.UseVerticalBoxLayout(() =>
            {
                _registerAsEntity.PoolName = this.WithTextField("Pool: ", _registerAsEntity.PoolName);
            });
        }

        private void ComponentListings()
        {
            EditorGUILayout.BeginVertical(EditorExtensions.DefaultBoxStyle);
            this.WithHorizontalLayout(() =>
            {
                this.WithLabel("Components (" + _registerAsEntity.Components.Count() + ")");
                if (this.WithIconButton("▸")) { showComponents = false; }
                if (this.WithIconButton("▾")) { showComponents = true; }
            });

            var componentsToRemove = new List<int>();
            if (showComponents)
            {
                for (var i = 0; i < _registerAsEntity.Components.Count(); i++)
                {
                    this.UseVerticalBoxLayout(() =>
                    {
                        var componentType = _registerAsEntity.Components[i];
                        var namePortions = componentType.Split(',')[0].Split('.');
                        var typeName = namePortions.Last();
                        var typeNamespace = string.Join(".", namePortions.Take(namePortions.Length - 1).ToArray());

                        this.WithVerticalLayout(() =>
                        {
                            this.WithHorizontalLayout(() =>
                            {
                                if (this.WithIconButton("-"))
                                {
                                    componentsToRemove.Add(i);
                                }

                                this.WithLabel(typeName);
                            });

                            EditorGUILayout.LabelField(typeNamespace);
                            EditorGUILayout.Space();
                        });

                        ComponentPropertiesDisplay.ShowComponentProperties(_registerAsEntity, i);
                    });
                }
            }

            EditorGUILayout.EndVertical();

            for (var i = 0; i < componentsToRemove.Count(); i++)
            {
                _registerAsEntity.Components.RemoveAt(componentsToRemove[i]);
                _registerAsEntity.Properties.RemoveAt(componentsToRemove[i]);
            }
        }
        

        private void ComponentSelectionSection()
        {
            this.UseVerticalBoxLayout(() =>
            {
                var availableTypes = allComponentTypes
                    .Where(x => !_registerAsEntity.Components.Contains(x.ToString()))
                    .ToArray();

                var types = availableTypes.Select(x => string.Format("{0} [{1}]", x.Name, x.Namespace)).ToArray();
                var index = -1;
                index = EditorGUILayout.Popup("Add Component", index, types);
                if (index >= 0)
                {
                    var component = availableTypes.ElementAt(index);
                    var componentName = component.ToString();
                    _registerAsEntity.Components.Add(componentName);
                    var json = component.SerializeComponent();
                    _registerAsEntity.Properties.Add(json.ToString());
                }
            });
        }

        private void PersistChanges()
        {
            if (GUI.changed)
            { this.SaveActiveSceneChanges(); }
        }

        public override void OnInspectorGUI()
        {
            _registerAsEntity = (RegisterAsEntity)target;
            
            PoolSection();
            EditorGUILayout.Space();
            ComponentSelectionSection();
            ComponentListings();
            PersistChanges();
            
        }
    }
}