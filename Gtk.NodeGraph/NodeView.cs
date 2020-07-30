// Copyright (C) 2019 Armin Luntzer (armin.luntzer@univie.ac.at)
//               Department of Astrophysics, University of Vienna
//
// C# port by Axel Nana <axel.nana@aliens-group.com>
// Copyright (C) 2020 Aliens Group LLC.
//
// The initial version of this project was developed as part of the
// activities under ESA/PRODEX contract number C4000126224.
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gdk;
using GLib;
using System.Linq;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Reflection;

namespace Gtk.NodeGraph
{
    /// <summary>
    /// The <see cref="NodeView"/> widget is a viewer and connection manager
    /// for <see cref="Node"/> widgets.
    /// </summary>
    public class NodeView : Container
    {
        #region Constants

        private const int ResizeRectangle = 16;

        #endregion

        #region Fields

        private static Dictionary<Type, string> _registeredNodeTypes = new Dictionary<Type, string>();

        private readonly List<NodeViewChild> _children = new List<NodeViewChild>();
        private readonly List<NodeViewConnection> _connections = new List<NodeViewConnection>();

        private Gdk.Window _eventWindow;

        private Cursor _cursorDefault;
        private Cursor _cursorSeResize;

        private NodeViewAction _action;

        private uint _nodeId;

        private int _x0, _y0;
        private int _x1, _y1;

        #endregion

        #region Constructors

        /// <summary>
        /// Static initializer.
        /// </summary>
        static NodeView()
        {
            RegisterNodeType<Node>("GtkNodesNode");
        }

        /// <summary>
        /// Creates a new <see cref="NodeView"/>.
        /// </summary>
        public NodeView()
        {
            CursorInit();

            HasWindow = false;
            SetSizeRequest(100, 100);

            Drag.DestSet(this, DestDefaults.Motion, null, DragAction.Private);
            Drag.DestSetTrackMotion(this, true);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Registers a <see cref="Node"/> subclass for saving/loading.
        /// </summary>
        /// <param name="nickname">The nickname of the node subclass in saved files.</param>
        /// <typeparam name="T">The node subclass type.</typeparam>
        public static void RegisterNodeType<T>(string nickname)
            where T : Node
        {
            RegisterNodeType(typeof(T), nickname);
        }

        /// <summary>
        /// Registers a <see cref="Node"/> subclass for saving/loading.
        /// </summary>
        /// <param name="type">The node subclass type.</param>
        /// <param name="nickname">The nickname of the node subclass in saved files.</param>
        public static void RegisterNodeType(Type type, string nickname)
        {
            if (_registeredNodeTypes.ContainsKey(type))
                return;

            if (type != typeof(Node) && !type.IsSubclassOf(typeof(Node)))
                throw new InvalidOperationException("The given type is not a child of the Node class");

            _registeredNodeTypes[type] = nickname;
        }

        private void ChildMotionNotifyEventHandler(object sender, MotionNotifyEventArgs args)
        {
            NodeViewChild child = GetChild((Widget) sender);
            Debug.Assert(child != null);

            if (_action == NodeViewAction.None)
            {
                bool inside = PointInRectangle(child.SouthEast, (int) args.Event.X, (int) args.Event.Y);
                NodeCursorSet(inside ? NodeViewAction.Resize : NodeViewAction.None);
            }

            if ((args.Event.State & ModifierType.Button1Mask) != 0)
            {
                if (_action == NodeViewAction.DragChild)
                {
                    ((Node) child.Child).BlockExpander();
                    MoveChild(child, (int) args.Event.X - child.DragStart.X, (int) args.Event.Y - child.DragStart.Y);
                }

                if (_action == NodeViewAction.Resize)
                {
                    int w = (int) args.Event.X - child.Rectangle.X - child.DragDelta.X;
                    int h = (int) args.Event.Y - child.Rectangle.Y - child.DragDelta.Y;

                    child.Rectangle.Width = Math.Max(0, w);
                    child.Rectangle.Height = Math.Max(0, h);

                    child.Width = child.Rectangle.Width;
                    child.Height = child.Rectangle.Height;

                    child.Child.QueueResize();
                    QueueDraw();
                }
            }

            args.RetVal = true;
        }

        private void ChildPointerCrossingEventHandler(object sender, LeaveNotifyEventArgs args)
        {
            args.RetVal = false;

            if (_action == NodeViewAction.Resize)
                return;

            switch (args.Event.Type)
            {
                case EventType.LeaveNotify:
                default:
                    NodeCursorSet(NodeViewAction.None);
                    break;
            }
        }

        private void ChildButtonPressEventHandler(object sender, ButtonPressEventArgs args)
        {
            NodeViewChild child = GetChild((Widget) sender);
            Debug.Assert(child != null);

            if (args.Event.Button == 1) // GDK_BUTTON_PRIMARY
            {
                int x = (int) args.Event.X;
                int y = (int) args.Event.Y;

                bool inside = PointInRectangle(child.SouthEast, x, y);
                _action = inside ? NodeViewAction.Resize : NodeViewAction.DragChild;

                child.DragStart = new Point(x, y);

                Rectangle childAlloc = child.Child.Allocation;
                child.DragDelta = new Point(x - (childAlloc.X + childAlloc.Width), y - (childAlloc.Y + childAlloc.Height));
            }

            args.RetVal = false;
        }

        private void ChildButtonReleaseEvent(object sender, ButtonReleaseEventArgs args)
        {
            NodeViewChild child = GetChild((Widget) sender);
            Debug.Assert(child != null);

            if (args.Event.Button == 1) // GDK_BUTTON_PRIMARY
                ((Node) child.Child).UnblockExpander();

            _action = NodeViewAction.None;

            args.RetVal = false;
        }

        private void NodeDragBeginEventHandler(object sender, NodeSocketDragEventArgs args)
        {
            _action = NodeViewAction.DragConnection;
            _x0 = args.X;
            _y0 = args.Y;
        }

        private void NodeDragEndEventHandler(object sender, EventArgs args)
        {
            _action = NodeViewAction.None;
            ((Widget) sender).Parent.QueueDraw();
        }

        private void NodeSocketConnectEventHandler(object sender, NodeSocketConnectionEventArgs args)
        {
            NodeViewConnection con = new NodeViewConnection
            {
                Source = args.Source,
                Sink = args.Sink,
            };

            _connections.Add(con);

            QueueDraw();
        }

        private void NodeSocketDisconnectEventHandler(object sender, NodeSocketConnectionEventArgs args)
        {
            foreach (NodeViewConnection connection in _connections.ToArray())
            {
                if (connection.Source != args.Source || connection.Sink != args.Sink)
                    continue;

                _connections.Remove(connection);
                break;
            }

            QueueDraw();
        }

        private void NodeSocketDestroyedEventHandler(object sender, NodeSocketDestroyedEventArgs args)
        {
            foreach (NodeViewConnection connection in _connections.ToArray())
            {
                if (connection.Source != args.Socket && connection.Sink != args.Socket)
                    continue;

                _connections.Remove(connection);
                break;
            }

            QueueDraw();
        }

        private void NodeConnectingCurve(Cairo.Context cr, int x0, int y0, int x1, int y1)
        {
            int x1m, y1m;
            int x2m, y2m;
            int d;

            cr.MoveTo(x0, y0);

            d = Math.Abs(x1 - x0) / 2;

            x1m = x0 + d;
            y1m = y0;

            x2m = x1 - d;
            y2m = y1;

            cr.CurveTo(x1m, y1m, x2m, y2m, x1, y1);
        }

        private void DrawSocketConnection(Cairo.Context cr, NodeViewConnection c)
        {
            Cairo.LinearGradient pat;
            RGBA colSrc, colSink;

            Rectangle allocation;
            Rectangle allocParent;
            int x0, x1, y0, y1;

            allocParent = c.Source.Parent.Allocation;
            allocation = c.Source.Allocation;
            x0 = allocation.X + allocation.Width / 2 + allocParent.X;
            y0 = allocation.Y + allocation.Height / 2 + allocParent.Y;

            allocParent = c.Sink.Parent.Allocation;
            allocation = c.Sink.Allocation;
            x1 = allocation.X + allocation.Width / 2 + allocParent.X;
            y1 = allocation.Y + allocation.Height / 2 + allocParent.Y;

            pat = new Cairo.LinearGradient(x0, y0, x1, y1);

            colSrc = c.Source.RGBA;
            colSink = c.Sink.RGBA;

            pat.AddColorStop
            (
                0,
                new Cairo.Color(colSrc.Red, colSrc.Green, colSrc.Blue, colSrc.Alpha)
            );

            pat.AddColorStop
            (
                1,
                new Cairo.Color(colSink.Red, colSink.Green, colSink.Blue, colSink.Alpha)
            );

            cr.Save();

            NodeConnectingCurve(cr, x0, y0, x1, y1);

            cr.SetSource(pat);
            cr.Stroke();
            pat.Dispose();

            cr.Restore();
        }

        private void MoveChild(NodeViewChild child, int x, int y)
        {
            int xMax, yMax;

            Rectangle allocationView = Allocation;
            Rectangle allocationChild = child.Child.Allocation;

            x += child.Rectangle.X;
            y += child.Rectangle.Y;

            xMax = allocationView.Width - allocationChild.Width;
            yMax = allocationView.Height - allocationChild.Height;

            // Keep child within node view allocation
            if (x > 0 && x < xMax)
                child.Rectangle.X = x;
            else if (x < 0)
                child.Rectangle.X = 0;
            else if (x > xMax)
                child.Rectangle.X = xMax;

            if (y > 0 && y < yMax)
                child.Rectangle.Y = y;
            else if (y < 0)
                child.Rectangle.Y = 0;
            else if (y > yMax)
                child.Rectangle.Y = yMax;

            child.X = child.Rectangle.X;
            child.Y = child.Rectangle.Y;

            if (child.Child.Visible)
                child.Child.QueueResize();

            // "raise" window, drawing occurs from start -> end of list
            _children.Remove(child);
            _children.Add(child);

            QueueDraw();
        }

        private bool PointInRectangle(in Rectangle rectangle, int x, int y)
        {
            return rectangle.Contains(x, y);
        }

        private NodeViewChild GetChild(Widget widget)
        {
            foreach (NodeViewChild child in from NodeViewChild child in _children
                                            where child.Child == widget
                                            select child)
            {
                return child;
            }

            Trace.WriteLine("Widget is not a child of NodeView");

            return null;
        }

        private void CursorInit()
        {
            Display display = Display;

            _cursorDefault = new Cursor(display, "default");
            _cursorSeResize = new Cursor(display, "se-resize");
        }

        private void NodeCursorSet(NodeViewAction action)
        {
            Gdk.Window window = Window;

            Cursor cursor;
            switch (action)
            {
                case NodeViewAction.Resize:
                {
                    cursor = _cursorSeResize;
                    break;
                }
                case NodeViewAction.None:
                default:
                {
                    cursor = _cursorDefault;
                    break;
                }
            }

            window.Cursor = cursor;
        }

        private void ConnectionMapper(Node o, string handlerName, Node connectObject)
        {
            string[] parts = handlerName.Split(new[] { '_' }, 2);
            uint idSource = uint.Parse(parts[0]);
            uint idSink = uint.Parse(parts[1]);

            NodeSocket source = null;
            NodeSocket sink = null;

            IReadOnlyList<NodeSocket> sockets = connectObject.Sources;
            foreach (NodeSocket socket in sockets)
            {
                if (socket.Id != idSource)
                    continue;

                source = socket;
                break;
            }

            sockets = o.Sinks;
            foreach (NodeSocket socket in sockets)
            {
                if (socket.Id != idSink)
                    continue;

                sink = socket;
                break;
            }

            if (source == null || sink == null)
                return;

            NodeSocket.ConnectSockets(sink, source);
        }

        /// <summary>
        /// Clears all <see cref="Node"/>s in this <see cref="NodeView"/>.
        /// </summary>
        public void Clear()
        {
            Widget[] children = Children;

            while (children.Length > 0)
            {
                Remove(children[0]);
                children = Children;
            }

            _nodeId = 0;
        }

        /// <summary>
        /// Saves a representation of the current node view setup as XML so
        /// it can be recreated with <see cref="Builder"/>.
        /// This only works properly for nodes which are their own widget types,
        /// as we don't (and can't) in-depth clone nodes
        /// </summary>
        /// <param name="filename">The name of the file to save, if the file exists, it will be overwritten</param>
        public void Save(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentNullException(nameof(filename), "No filename specified");

            using FileStream f = File.Open(filename, FileMode.Create);

            StringBuilder s = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                OmitXmlDeclaration = false,
            };

            using XmlWriter xmlWriter = XmlWriter.Create(f, settings);

            void WritePropertyElement(string name, string value)
            {
                xmlWriter.WriteStartElement("property");
                xmlWriter.WriteAttributeString("name", name);
                xmlWriter.WriteString(value);
                xmlWriter.WriteEndElement();
            }

            void WriteSignalElement(string name, string handler, string o)
            {
                xmlWriter.WriteStartElement("signal");
                xmlWriter.WriteAttributeString("name", name);
                xmlWriter.WriteAttributeString("handler", handler);
                xmlWriter.WriteAttributeString("object", o);
                xmlWriter.WriteEndElement();
            }

            // Lead in
            xmlWriter.WriteStartDocument();
            xmlWriter.WriteStartElement("interface");

            // Fixup the IDs so we can properly load, add and save again
            // XXX I really need to think of a better method for unique IDs
            _nodeId = 0;
            foreach (NodeViewChild child in _children)
            {
                if (!(child.Child is Node node))
                    continue;

                node.Id = _nodeId++;
            }

            foreach (NodeViewChild child in _children)
            {
                if (!(child.Child is Node node))
                    continue;

                Type nodeType = child.Child.GetType();
                if (!_registeredNodeTypes.TryGetValue(nodeType, out string nodeNickname))
                    throw new InvalidOperationException($"The node type {nodeType.Name} is not registered in this node view.");

                xmlWriter.WriteStartElement("object");
                xmlWriter.WriteAttributeString("class", nodeNickname);
                xmlWriter.WriteAttributeString("id", node.Id.ToString());

                WritePropertyElement(Node.LabelProperty, node.Label);
                WritePropertyElement(Node.PaddingBottomProperty, node.PaddingBottom.ToString());
                WritePropertyElement(Node.PaddingLeftProperty, node.PaddingLeft.ToString());
                WritePropertyElement(Node.PaddingRightProperty, node.PaddingRight.ToString());
                WritePropertyElement(Node.PaddingTopProperty, node.PaddingTop.ToString());
                WritePropertyElement(Node.SocketRadiusProperty, node.SocketRadius.ToString());
                WritePropertyElement(Node.XProperty, node.X.ToString());
                WritePropertyElement(Node.YProperty, node.Y.ToString());
                WritePropertyElement(Node.WidthProperty, node.Width.ToString());
                WritePropertyElement(Node.HeightProperty, node.Height.ToString());
                WritePropertyElement(Node.IdProperty, node.Id.ToString());

                foreach (NodeSocket socket in node.Sinks)
                {
                    NodeSocket input = socket.Input;
                    if (input == null)
                        continue;

                    // We'll save the socket ids in the name of the handler and
                    // reconstruct them in ConnectionMapper()
                    // this way we can (ab)use Builder to do most of the work for us
                    WriteSignalElement(Node.NodeSocketConnectSignal, $"{input.Id}_{socket.Id}", ((Node) input.GetAncestor(Node.GType)).Id.ToString());
                }

                XmlNode[] internalCfg = node.ExportProperties();

                if (internalCfg != null)
                {
                    foreach (XmlNode cfg in internalCfg)
                        cfg.WriteTo(xmlWriter);
                }

                xmlWriter.WriteEndElement();
            }

            xmlWriter.WriteEndElement();
            xmlWriter.WriteEndDocument();

            xmlWriter.Close();
        }

        /// <summary>
        /// Saves a representation of the current node view setup as XML so
        /// it can be recreated with <see cref="Builder"/>.
        /// This only works properly for nodes which are their own widget types,
        /// as we don't (and can't) in-depth clone nodes
        /// </summary>
        /// <param name="filename">The name of the file to save, if the file exists, it will be overwritten</param>
        /// <returns>
        /// <c>false</c> on error.
        /// </returns>
        public bool TrySave(string filename)
        {
            try
            {
                Save(filename);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Loads an XML description parseable by <see cref="Builder"/> and reconstructs
        /// a node configuration. Note that this will not restore the internal
        /// state of any nodes, but only their placement and the connections between sockets.
        /// </summary>
        /// <remarks>
        /// This only works properly for nodes which are their own widget types.
        /// </remarks>
        /// <param name="filename">The name of the file to load.</param>
        public void Load(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                throw new ArgumentNullException(nameof(filename), "No filename specified");

            using FileStream f = File.OpenRead(filename);

            Clear();

            XmlReaderSettings settings = new XmlReaderSettings
            {
                CloseInput = true,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };

            using XmlReader reader = XmlReader.Create(f, settings);

            reader.ReadStartElement("interface");

            List<(string, uint, uint)> connections = new List<(string, uint, uint)>();
            Dictionary<uint, Node> nodes = new Dictionary<uint, Node>();

            while (reader.Name == "object")
            {
                string className = reader.GetAttribute("class");

                if (!_registeredNodeTypes.ContainsValue(className))
                    throw new InvalidOperationException($"Unable to load the file. Found an unregistered node type: {className}");

                Type classType = _registeredNodeTypes.First(t => t.Value == className).Key;
                if (!(Activator.CreateInstance(classType) is Node node))
                    throw new InvalidOperationException($"The registered node type {className} is not a subtype of the Node class.");

                MemberInfo[] members = classType
                    .GetMembers(BindingFlags.GetField | BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes<Builder.ObjectAttribute>().Any())
                    .ToArray();

                reader.ReadStartElement("object");

                while (reader.Name == "property")
                {
                    XElement p = XNode.ReadFrom(reader) as XElement;
                    string propertyName = p.FirstAttribute.Value;
                    switch (propertyName)
                    {
                        case Node.LabelProperty:
                        {
                            node.Label = p.Value;
                            break;
                        }
                        case Node.XProperty:
                        {
                            node.X = int.Parse(p.Value);
                            break;
                        }
                        case Node.YProperty:
                        {
                            node.Y = int.Parse(p.Value);
                            break;
                        }
                        case Node.WidthProperty:
                        {
                            node.Width = uint.Parse(p.Value);
                            break;
                        }
                        case Node.HeightProperty:
                        {
                            node.Height = uint.Parse(p.Value);
                            break;
                        }
                        case Node.IdProperty:
                        {
                            node.Id = uint.Parse(p.Value);
                            break;
                        }
                        case Node.PaddingBottomProperty:
                        {
                            node.PaddingBottom = short.Parse(p.Value);
                            break;
                        }
                        case Node.PaddingLeftProperty:
                        {
                            node.PaddingLeft = short.Parse(p.Value);
                            break;
                        }
                        case Node.PaddingRightProperty:
                        {
                            node.PaddingRight = short.Parse(p.Value);
                            break;
                        }
                        case Node.PaddingTopProperty:
                        {
                            node.PaddingTop = short.Parse(p.Value);
                            break;
                        }
                        case Node.SocketRadiusProperty:
                        {
                            node.SocketRadius = double.Parse(p.Value);
                            break;
                        }
                    }
                }

                while (reader.Name == "signal")
                {
                    XElement s = XNode.ReadFrom(reader) as XElement;
                    string handler = s.Attribute(XName.Get("handler")).Value;
                    uint connectedObject = uint.Parse(s.Attribute(XName.Get("object")).Value);

                    connections.Add((handler, node.Id, connectedObject));
                }

                while (reader.Name == "child")
                {
                    XElement c = XNode.ReadFrom(reader) as XElement;
                    string innerDOM = c.Elements().Aggregate(string.Empty, (s, e) => s + e.ToString());
                    Builder b = new Builder();
                    b.AddFromString("<interface>" + innerDOM + "</interface>");

                    foreach (MemberInfo member in members)
                    {
                        GLib.Object o = b.GetObject(member.Name);
                        if (o == null)
                            continue;

                        switch (member.MemberType)
                        {
                            case MemberTypes.Property:
                            {
                                PropertyInfo property = (PropertyInfo) member;
                                property.SetValue(node, Activator.CreateInstance(property.PropertyType, o));
                                break;
                            }
                            case MemberTypes.Field:
                            {
                                FieldInfo field = (FieldInfo) member;
                                ConstructorInfo constructor = field.FieldType.GetConstructor(new Type[] { typeof(IntPtr) });
                                field.SetValue(node, constructor.Invoke(new object[] { o.Handle }));
                                break;
                            }
                        }
                    }
                }

                reader.ReadEndElement();

                Add(node);
                nodes.Add(node.Id, node);
            }

            reader.ReadEndElement();

            foreach ((string handler, Node current, Node other) in connections.Select(t => (t.Item1, nodes[t.Item2], nodes[t.Item3])))
                ConnectionMapper(current, handler, other);
        }

        #endregion

        #region Container Implementation

        protected override void OnAdded(Widget widget)
        {
            // The things we do for glade...
            // XXX @na2axl: Glade support ?
            if (widget is Node node)
            {
                node.NodeSocketDragBeginEvent += NodeDragBeginEventHandler;
                node.NodeSocketDragEndEvent += NodeDragEndEventHandler;
                node.NodeSocketConnectEvent += NodeSocketConnectEventHandler;
                node.NodeSocketDisconnectEvent += NodeSocketDisconnectEventHandler;
                node.NodeSocketDestroyedEvent += NodeSocketDestroyedEventHandler;

                node.Id = _nodeId++;

                node.OnInitChildren();
            }

            NodeViewChild child = new NodeViewChild(this, widget)
            {
                Rectangle = new Rectangle(100, 100, 100, 100),
                SouthEast = new Rectangle(100 - ResizeRectangle, 100 - ResizeRectangle, ResizeRectangle, ResizeRectangle),
            };

            widget.AddEvents((int) (EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.Button1MotionMask));

            widget.ButtonPressEvent += ChildButtonPressEventHandler;
            widget.ButtonReleaseEvent += ChildButtonReleaseEvent;
            widget.MotionNotifyEvent += ChildMotionNotifyEventHandler;
            widget.LeaveNotifyEvent += ChildPointerCrossingEventHandler;

            _children.Add(child);

            if (IsRealized)
                widget.ParentWindow = _eventWindow;

            widget.Parent = this;

            ShowAll();
        }

        protected override void OnRemoved(Widget widget)
        {
            NodeViewChild item = null;
            foreach (NodeViewChild child in _children)
            {
                if (child.Child != widget)
                    continue;

                item = child;
                break;
            }

            if (item == null)
                return;

            _children.Remove(item);

            widget.Unparent();
        }

        protected override void ForAll(bool include_internals, Callback callback)
        {
            foreach (NodeViewChild child in _children.ToArray())
                callback?.Invoke(child.Child);
        }

        protected override GType OnChildType()
        {
            return Node.GType;
        }

        protected override void OnSetChildProperty(Widget child, uint property_id, Value value, IntPtr pspec)
        {
            NodeViewChild nodeChild = GetChild(child);

            if (nodeChild == null)
                return;

            // This is kinda stupid, but at the moment I don't see how I can save an
            // XML description of the node view's contents and restore a (partial)
            // configuration without tracking the child properties within the
            // node children as well

            switch (property_id)
            {
                case NodeViewChild.ChildPropertyXId:
                {
                    nodeChild.X = (int) value.Val;
                    break;
                }

                case NodeViewChild.ChildPropertyYId:
                {
                    nodeChild.Y = (int) value.Val;
                    break;
                }

                case NodeViewChild.ChildPropertyWidthId:
                {
                    nodeChild.Width = (int) value.Val;
                    break;
                }

                case NodeViewChild.ChildPropertyHeightId:
                {
                    nodeChild.Height = (int) value.Val;
                    break;
                }

                default:
                    throw new InvalidOperationException("Invalid property ID");
            }
        }

        protected override void OnGetChildProperty(Widget child, uint property_id, Value value, IntPtr pspec)
        {
            NodeViewChild nodeChild = GetChild(child);

            if (nodeChild == null)
                return;

            switch (property_id)
            {
                case NodeViewChild.ChildPropertyXId:
                {
                    value.Val = nodeChild.X;
                    break;
                }

                case NodeViewChild.ChildPropertyYId:
                {
                    value.Val = nodeChild.Y;
                    break;
                }

                case NodeViewChild.ChildPropertyWidthId:
                {
                    value.Val = nodeChild.Width;
                    break;
                }

                case NodeViewChild.ChildPropertyHeightId:
                {
                    value.Val = nodeChild.Height;
                    break;
                }

                default:
                    throw new InvalidOperationException("Invalid property ID");
            }
        }

        #endregion

        #region Widget Implementation

        protected override void OnMapped()
        {
            IsMapped = true;

            foreach (NodeViewChild child in _children)
            {
                if (!child.Child.IsVisible)
                    continue;

                if (!child.Child.IsMapped)
                    child.Child.Map();
            }

            _eventWindow?.Show();
        }

        protected override void OnUnmapped()
        {
            _eventWindow?.Hide();

            base.OnUnmapped();
        }

        protected override void OnRealized()
        {
            IsRealized = true;
            Rectangle allocation = Allocation;

            WindowAttr attributes = new WindowAttr
            {
                WindowType = Gdk.WindowType.Child,
                X = allocation.X,
                Y = allocation.Y,
                Width = allocation.Width,
                Height = allocation.Height,
                Wclass = WindowWindowClass.InputOutput,
                EventMask = (int) (Events
                    | EventMask.ButtonPressMask
                    | EventMask.ButtonReleaseMask
                    | EventMask.PointerMotionMask
                    | EventMask.TouchMask
                    | EventMask.EnterNotifyMask
                    | EventMask.LeaveNotifyMask)
            };

            Gdk.Window window = ParentWindow;
            Window = window;

            _eventWindow = new Gdk.Window(window, attributes, (int) (WindowAttributesType.X | WindowAttributesType.Y));
            RegisterWindow(_eventWindow);

            foreach (NodeViewChild child in _children)
                child.Child.ParentWindow = _eventWindow;
        }

        protected override void OnUnrealized()
        {
            if (_eventWindow != null)
            {
                UnregisterWindow(_eventWindow);
                _eventWindow.Destroy();
                _eventWindow = null;
            }

            base.OnUnrealized();
        }

        protected override void OnSizeAllocated(Rectangle allocation)
        {
            foreach (NodeViewChild child in _children)
            {
                int w, h, socketRadius;

                child.Child.GetPreferredSize(out Requisition requisition, out _);

                if (child.Child is Node node)
                    socketRadius = (int) node.SocketRadius;
                else
                    socketRadius = 0;

                Rectangle allocationChild = new Rectangle
                (
                    child.X,
                    child.Y,
                    Math.Max(requisition.Width + child.PaddingLeft + child.PaddingRight + socketRadius * 2, child.Width),
                    Math.Max(requisition.Height + child.PaddingTop + child.PaddingBottom + socketRadius * 2, child.Height)
                );

                child.Child.SizeAllocate(allocationChild);
                allocationChild = child.Child.Allocation;

                child.SouthEast.X = allocationChild.Width - socketRadius - ResizeRectangle;
                child.SouthEast.Y = allocationChild.Height - socketRadius - ResizeRectangle;

                w = allocationChild.X + allocationChild.Width;
                h = allocationChild.Y + allocationChild.Height;

                if (w > allocation.Width)
                    allocation.Width = w;

                if (h > allocation.Height)
                    allocation.Height = h;
            }

            SetAllocation(allocation);
            SetSizeRequest(allocation.Width, allocation.Height);

            if (!IsRealized)
                return;

            if (_eventWindow == null)
                return;

            _eventWindow.MoveResize
            (
                allocation.X,
                allocation.Y,
                allocation.Width,
                allocation.Height
            );
        }

        protected override bool OnDrawn(Cairo.Context cr)
        {
            if (_action == NodeViewAction.DragConnection)
            {
                cr.Save();
                cr.SetSourceRGBA(1, 0.2, 0.2, 0.6);

                NodeConnectingCurve(cr, _x0, _y0, _x1, _y1);

                cr.Stroke();
                cr.Restore();
            }

            foreach (NodeViewConnection connection in _connections)
                DrawSocketConnection(cr, connection);

            if (CairoHelper.ShouldDrawWindow(cr, _eventWindow))
                base.OnDrawn(cr);

            return false;
        }

        protected override bool OnDragMotion(DragContext context, int x, int y, uint time_)
        {
            _x1 = x;
            _y1 = y;

            QueueDraw();

            return false;
        }

        protected override bool OnButtonPressEvent(EventButton e)
        {
            base.OnButtonPressEvent(e);

            return false;
        }

        protected override bool OnMotionNotifyEvent(EventMotion e)
        {
            base.OnMotionNotifyEvent(e);

            return false;
        }

        #endregion

        #region Node View Child

        private class NodeViewChild : ContainerChild
        {
            #region GTK Constants

            public const string XProperty = "x";

            public const string YProperty = "y";

            public const string WidthProperty = "width";

            public const string HeightProperty = "height";

            /// <summary>
            /// Property storing the left padding value of this child.
            /// </summary>
            public const string PaddingLeftProperty = "padding-left";

            /// <summary>
            /// Property storing the right padding value of this child.
            /// </summary>
            public const string PaddingRightProperty = "padding-right";

            /// <summary>
            /// Property storing the top padding value of this child.
            /// </summary>
            public const string PaddingTopProperty = "padding-top";

            /// <summary>
            /// Property storing the bottom padding value of this child.
            /// </summary>
            public const string PaddingBottomProperty = "padding-bottom";

            public const uint ChildPropertyXId = 1;
            public const uint ChildPropertyYId = 2;
            public const uint ChildPropertyWidthId = 3;
            public const uint ChildPropertyHeightId = 4;

            #endregion

            #region Fields

            public Rectangle Rectangle;
            public Rectangle SouthEast;

            public Point DragStart;
            public Point DragDelta;

            #endregion

            #region Properties

            [ChildProperty(XProperty)]
            public int X
            {
                get
                {
                    if (Child is Node node)
                        Rectangle.X = node.X;

                    return Rectangle.X;
                }
                set
                {
                    Rectangle.X = value;
                    if (Child is Node node)
                        node.X = value;

                    Child.QueueAllocate();
                }
            }

            [ChildProperty(YProperty)]
            public int Y
            {
                get
                {
                    if (Child is Node node)
                        Rectangle.Y = node.Y;

                    return Rectangle.Y;
                }
                set
                {
                    Rectangle.Y = value;
                    if (Child is Node node)
                        node.Y = value;

                    Child.QueueAllocate();
                }
            }

            [ChildProperty(WidthProperty)]
            public int Width
            {
                get
                {
                    if (Child is Node node)
                        Rectangle.Width = (int) node.Width;

                    return Rectangle.Width;
                }
                set
                {
                    Rectangle.Width = value;
                    if (Child is Node node)
                        node.Width = (uint) value;

                    Child.QueueAllocate();
                }
            }

            [ChildProperty(HeightProperty)]
            public int Height
            {
                get
                {
                    if (Child is Node node)
                        Rectangle.Height = (int) node.Height;

                    return Rectangle.Height;
                }
                set
                {
                    Rectangle.Height = value;
                    if (Child is Node node)
                        node.Height = (uint) value;

                    Child.QueueAllocate();
                }
            }

            [ChildProperty(PaddingBottomProperty)]
            public short PaddingBottom
            {
                get
                {
                    return (Child is Node node) ? node.PaddingBottom : (short) 0;
                }
                set
                {
                    if (Child is Node node)
                        node.PaddingBottom = value;
                }
            }

            [ChildProperty(PaddingTopProperty)]
            public short PaddingTop
            {
                get
                {
                    return (Child is Node node) ? node.PaddingTop : (short) 0;
                }
                set
                {
                    if (Child is Node node)
                        node.PaddingTop = value;
                }
            }

            [ChildProperty(PaddingLeftProperty)]
            public short PaddingLeft
            {
                get
                {
                    return (Child is Node node) ? node.PaddingLeft : (short) 0;
                }
                set
                {
                    if (Child is Node node)
                        node.PaddingLeft = value;
                }
            }

            [ChildProperty(PaddingRightProperty)]
            public short PaddingRight
            {
                get
                {
                    return (Child is Node node) ? node.PaddingRight : (short) 0;
                }
                set
                {
                    if (Child is Node node)
                        node.PaddingRight = value;
                }
            }

            #endregion

            #region Constructors

            public NodeViewChild(Container parent, Widget child)
                : base(parent, child)
            { }

            #endregion
        }

        #endregion

        #region Node View Connection

        private class NodeViewConnection
        {
            public NodeSocket Source { get; set; }

            public NodeSocket Sink { get; set; }
        }

        #endregion
    }
}
