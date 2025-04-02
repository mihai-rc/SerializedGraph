using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GiftHorse.SerializedGraphs.Editor
{
    /// <summary>
    /// <see cref="GraphView"/> class used to display the <see cref="SerializedGraphBase"/> in the editor.
    /// </summary>
    public class ScriptableGraphView : GraphView
    {
        private const string k_UssFilePath = "Packages/com.gift-horse.scriptable-graph/Editor/Uss/ScriptableGraphView.uss";
        private const string k_UssFileNotFoundError = "[Editor] [SerializedGraph] No uss file was found at: {0}.";
        private const string k_BackgroundName = "Background";
        private const string k_AddNodeUndoRecord = "Added Graph Node";
        private const string k_AddConnectionUndoRecord = "Added Graph Connections";
        private const string k_MoveNodeUndoRecord = "Moved Graph Nodes";
        private const string k_RemoveElementUndoRecord = "Removed Graph Element";

        private readonly ScriptableGraphEditorContext m_Context;
        private readonly List<ScriptableNodeView> m_NodeViews = new();
        private readonly List<Edge> m_EdgeViews = new();
        private readonly Dictionary<string, ScriptableNodeView> m_NodeViewsByNodeId = new();

        /// <summary>
        /// <see cref="ScriptableGraphView"/>'s constructor.
        /// </summary>
        /// <param name="context"> Reference to the <see cref="SearchWindowContext"/> to access relevant dependencies and draw the graph. </param>
        public ScriptableGraphView(ScriptableGraphEditorContext context)
        {
            m_Context = context;

            LoadUssFile();
            SetupBackground();
            SetupManipulators();
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            DrawNodes();
            DrawConnections();
            UpdateExpandedNodes();

            graphViewChanged += OnGraphViewChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        ~ScriptableGraphView()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        /// <inheritdoc />
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var allPorts = new List<Port>();
            var compatiblePorts = new List<Port>();
            foreach (var node in m_NodeViews)
            {
                allPorts.AddRange(node.InPorts);
                allPorts.AddRange(node.OutPorts);
            }

            foreach (var port in allPorts)
            {
                if (port == startPort) continue;
                if (port.node == startPort.node) continue;
                if (port.direction == startPort.direction) continue;
                if (port.portType != startPort.portType) continue;

                compatiblePorts.Add(port);
            }

            return compatiblePorts;
        }

        /// <summary>
        /// Adds a <see cref="ISerializedNode"/> to the graph data structure when an entry is selected from <see cref="NodeSearchWindow"/>.
        /// </summary>
        /// <param name="node"> Reference to the newly created node. </param>
        public void Add(ISerializedNode node)
        {
            Undo.RecordObject(m_Context.SerializedObject.targetObject, k_AddNodeUndoRecord);

            m_Context.Graph.AddNode(node);
            m_Context.SerializedObject.Update();

            AddNodeToGraph(node);
            this.Bind(m_Context.SerializedObject);
        }

        private void LoadUssFile()
        {
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(k_UssFilePath);
            
            if (uss is null)
                Debug.LogErrorFormat(k_UssFileNotFoundError, k_UssFilePath);

            styleSheets.Add(uss);
        }

        private void SetupBackground()
        {
            var background = new GridBackground { name = k_BackgroundName };
            Add(background);
            background.SendToBack();
        }

        private void SetupManipulators()
        {
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());
        }

        private void DrawNodes()
        {
            foreach (var node in m_Context.Graph.Nodes)
                AddNodeToGraph(node);

            this.Bind(m_Context.SerializedObject);
        }

        private void DrawConnections()
        {
            if (m_Context.Graph.Connections is null)
                return;

            foreach (var connection in m_Context.Graph.Connections)
                DrawConnection(connection);
        }

        private void DrawConnection(Connection connection)
        {
            var fromPort = connection.FromPort;
            var toPort = connection.ToPort;

            m_NodeViewsByNodeId.TryGetValue(fromPort.NodeId, out var fromNodeView);
            m_NodeViewsByNodeId.TryGetValue(toPort.NodeId, out var toNodeView);

            var fromPortView = fromNodeView.OutPorts[fromPort.Index];
            var toPortView = toNodeView.InPorts[toPort.Index];
            var edge = fromPortView.ConnectTo(toPortView);
            m_EdgeViews.Add(edge);

            AddElement(edge);
        }

        private void UpdateExpandedNodes()
        {
            foreach (var node in m_NodeViews)
                node.expanded = node.SerializedNodeBase.Expanded;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.movedElements is not null)
            {
                Undo.RecordObject(m_Context.SerializedObject.targetObject, k_MoveNodeUndoRecord);

                foreach (var movedNode in graphViewChange.movedElements.OfType<ScriptableNodeView>())
                    movedNode.SavePosition();
            }

            if (graphViewChange.elementsToRemove is not null)
            {
                Undo.RecordObject(m_Context.SerializedObject.targetObject, k_RemoveElementUndoRecord);

                foreach (var removedEdge in graphViewChange.elementsToRemove.OfType<Edge>())
                    RemoveConnection(removedEdge);

                foreach (var removedNode in graphViewChange.elementsToRemove.OfType<ScriptableNodeView>())
                    RemoveNodeFromGraph(removedNode);
            }

            if (graphViewChange.edgesToCreate is not null)
            {
                Undo.RegisterCompleteObjectUndo(m_Context.SerializedObject.targetObject, k_AddConnectionUndoRecord);

                foreach (var edge in graphViewChange.edgesToCreate)
                    CreateConnection(edge);
            }

            return graphViewChange;
        }

        private void OnUndoRedoPerformed()
        {
            foreach (var edge in m_EdgeViews)
                RemoveElement(edge);

            foreach (var node in m_NodeViews)
                RemoveElement(node);

            m_EdgeViews.Clear();
            m_NodeViews.Clear();
            m_NodeViewsByNodeId.Clear();
            m_Context.Graph.UpdateMappings();

            DrawNodes();
            DrawConnections();
        }

        private void AddNodeToGraph(ISerializedNode node)
        {
            var isSearchableNode = ReflectionHelper.IsNodeExcludedFromSearch(node.GetType());
            var nodeView = new ScriptableNodeView(node, m_Context, !isSearchableNode);
            nodeView.SetPosition(node.Position);

            m_NodeViews.Add(nodeView);
            m_NodeViewsByNodeId.Add(node.Id, nodeView);

            AddElement(nodeView);
        }

        private void RemoveNodeFromGraph(ScriptableNodeView nodeView)
        {
            m_Context.Graph.RemoveNode(nodeView.SerializedNodeBase);
            m_Context.SerializedObject.Update();

            m_NodeViewsByNodeId.Remove(nodeView.SerializedNodeBase.Id);
            m_NodeViews.Remove(nodeView);
        }

        private void CreateConnection(Edge edge)
        {
            var toNodeView = edge.input.node as ScriptableNodeView;
            var toIndex = toNodeView.InPorts.IndexOf(edge.input);
            var toNode = toNodeView.SerializedNodeBase;

            var fromNodeView = edge.output.node as ScriptableNodeView;
            var fromIndex = fromNodeView.OutPorts.IndexOf(edge.output);
            var fromNode = fromNodeView.SerializedNodeBase;

            m_EdgeViews.Add(edge);
            m_Context.Graph.ConnectNodes(fromNode, fromIndex, toNode, toIndex);
        }

        private void RemoveConnection(Edge edge)
        {
            var toNodeView = edge.input.node as ScriptableNodeView;
            var toIndex = toNodeView.InPorts.IndexOf(edge.input);
            var toNode = toNodeView.SerializedNodeBase;

            var fromNodeView = edge.output.node as ScriptableNodeView;
            var fromIndex = fromNodeView.OutPorts.IndexOf(edge.output);
            var fromNode = fromNodeView.SerializedNodeBase;

            m_Context.Graph.DisconnectNodes(fromNode, fromIndex, toNode, toIndex);
        }
    }
}