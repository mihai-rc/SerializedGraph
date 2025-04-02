using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine;

namespace GiftHorse.SerializedGraphs.Editor
{
    /// <summary>
    /// <see cref="Node"/> class used to display a <see cref="ISerializedNode"/> in the graph editor.
    /// </summary>
    public class ScriptableNodeView : Node
    {
        private const string k_PropertiesHolderName = "PropertiesHolder";
        private const string k_PropertyFieldName = "PropertyField";
        private const string k_IdProperty = "m_Id";
        private const string k_NodesProperty = "m_Nodes";
        private const string k_SerializedPropertyNotFoundError = "[Editor] [SerializedGraph] Could not find the SerializedProperty of ScriptableGraphNode: {0}, Id: {1}.";
        private const string k_InvalidNodeProperty = "[Editor] [SerializedGraph] Could not find the relative property by name: {0}. Make sure you use [NodeField] attribute only with serialized types.";
        
        private readonly ISerializedNode m_SerializedNode;
        private readonly ScriptableGraphEditorContext m_Context;
        private SerializedProperty m_SerializedProperty;
        private VisualElement m_PropertiesHolder;
        
        /// <summary>
        /// Reference to the <see cref="ISerializedNode"/> this view is handling.
        /// </summary>
        public ISerializedNode SerializedNodeBase => m_SerializedNode;

        /// <summary>
        /// List of all <see cref="InPort"/>s of this node.
        /// </summary>
        public List<Port> InPorts { get; } = new();
        
        /// <summary>
        /// List of all <see cref="OutPort"/>s of this node.
        /// </summary>
        public List<Port> OutPorts { get; } = new();

        /// <summary>
        /// <see cref="ScriptableNodeView"/>'s constructor.
        /// </summary>
        /// <param name="node"> Reference to the <see cref="ISerializedNode"/> this view is handling. </param>
        /// <param name="context"> Reference to the <see cref="SearchWindowContext"/> to access relevant dependencies. </param>
        /// <param name="isDeletable"> Flag representing whether the user can or cannot delete the node from the <see cref="ScriptableGraphView"/>. </param>
        public ScriptableNodeView(ISerializedNode node, ScriptableGraphEditorContext context, bool isDeletable)
        {
            m_SerializedNode = node;
            m_Context = context;
            
            // Remove the delete capability
            if (!isDeletable)
                capabilities &= ~Capabilities.Deletable;
            
            CreateInputs();
            CreateOutputs();
            InitializeNodeByReflection();
        }
        
        /// <summary>
        /// Saves the position of this node after the node was moved in the editor and the user saves.
        /// </summary>
        public void SavePosition()
        {
            SerializedNodeBase.Position = GetPosition();
        }

        protected override void ToggleCollapse()
        {
            base.ToggleCollapse();
            
            m_SerializedNode.Expanded = expanded;
            m_Context.MarkAssetAsDirty();
        }

        private VisualElement InitializePropertiesHolder()
        {
            var propertiesHolder = new VisualElement();
            propertiesHolder.name = k_PropertiesHolderName;
            
            return propertiesHolder;
        }
        
        private SerializedProperty InitializeSerializedProperty()
        {
            m_Context.SerializedObject.Update();
            
            var nodes = m_Context.SerializedObject.FindProperty(k_NodesProperty);
            if (!nodes.isArray)
            {
                Debug.LogErrorFormat(k_SerializedPropertyNotFoundError, name, m_SerializedNode.Id);
                return null;
            }
            
            var size = nodes.arraySize;
            for (var i = 0; i < size; i++)
            {
                var element = nodes.GetArrayElementAtIndex(i);
                var elementId = element.FindPropertyRelative(k_IdProperty);
                    
                if (!elementId.stringValue.Equals(SerializedNodeBase.Id)) 
                    continue;
                    
                return element;
            }

            Debug.LogErrorFormat(k_SerializedPropertyNotFoundError, name, m_SerializedNode.Id);
            return null;
        }

        private void SetupNodeHeaderByReflection(Type type)
        {
            name = SerializedNodeBase.Title;
            title = SerializedNodeBase.Title;
            
            if (!ReflectionHelper.TryGetNodeHeaderColor(type, out var color)) 
                return;
            
            titleContainer.style.backgroundColor = new StyleColor(color);
            titleContainer.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        private void InitializeNodeByReflection()
        {
            var type = SerializedNodeBase.GetType();
            SetupNodeHeaderByReflection(type);
            GetNodePropertiesByReflection(type);
        }

        private void CreateInputs()
        {
            foreach (var inPort in m_SerializedNode.InPorts)
            {
                CreateInputPort(inPort.Name, Type.GetType(inPort.CompatibleType), false);
            }
        }
        
        private void CreateOutputs()
        {
            foreach (var outPort in m_SerializedNode.OutPorts)
            {
                CreateOutputPort(outPort.Name, Type.GetType(outPort.CompatibleType), true);
            }
        }

        private void GetNodePropertiesByReflection(Type type)
        {
            foreach (var propertyName in ReflectionHelper.GetNodeExposedFieldsNames(type))
            {
                if (m_PropertiesHolder is null)
                {
                    m_PropertiesHolder = InitializePropertiesHolder();
                }
                
                DrawProperty(propertyName);
            }
            
            extensionContainer.Add(m_PropertiesHolder);
            RefreshExpandedState();
        }

        private void CreateInputPort(string portName, Type portType, bool multiple = false)
        {
            var capacity = multiple ? Port.Capacity.Multi : Port.Capacity.Single;
            var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, capacity, portType);
            inputPort.portName = portName;
            
            InPorts.Add(inputPort);
            inputContainer.Add(inputPort);
        }

        private void CreateOutputPort(string portName, Type portType, bool multiple = false)
        {
            var capacity = multiple ? Port.Capacity.Multi : Port.Capacity.Single;
            var outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, capacity, portType);
            outputPort.portName = portName;
            
            OutPorts.Add(outputPort);
            outputContainer.Add(outputPort);
        }
        
        private void DrawProperty(string propertyName)
        {
            if (m_SerializedProperty is null)
            {
                m_SerializedProperty = InitializeSerializedProperty();
            }

            var property = m_SerializedProperty.FindPropertyRelative(propertyName);
            if (property is null)
            {
                Debug.LogErrorFormat(k_InvalidNodeProperty, propertyName);
                return;
            }

            var propertyField = new PropertyField(property);
            propertyField.name = k_PropertyFieldName;
            propertyField.Bind(property.serializedObject);
            
            m_PropertiesHolder.Add(propertyField);
        }
    }
}
