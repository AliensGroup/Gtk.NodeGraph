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
using System.Linq;
using System.Xml;
using Gdk;
using GLib;

namespace Gtk.NodeGraph
{
    /// <summary>
    /// <para>
    /// The <see cref="Node"/> widget is a widget container derived from <see cerf="Box"/>.
    /// Widgets added to the node are assigned a <see cref="NodeSocket"/>. The user must
    /// configure the type of socket and connect to the :socket-incoming signal
    /// in order to able to receive the data passed through a connection.
    /// </para>
    /// <para>
    /// The node can be collapsed by clicking the <see cref="Expander"/> which will hide the
    /// node items, and show a compact representation of the node with just the
    /// <see cref="Expander"/> and the sockets visible.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// While possible, using a <see cref="Node"/> outside of a <see cref="NodeView"/> does
    /// not make much sense.
    /// </para>
    /// <para>
    /// The placement of sockets is currently only properly supported for the
    /// <see cref="Orientation.Vertical"/> orientation.
    /// </para>
    /// <para>
    /// Custom <see cref="Node"/> widgets can save and restore their internal child
    /// widgets states and other properties and special tags by implementing
    /// the proper Buildable interfaces. To export the configuration,
    /// <see cref="ExportProperties"/> must be implemented and is expected to return
    /// valid XML output which is integrated into the XML output produces
    /// by <see cref="NodeView"/>.
    /// </para>
    /// <para>
    /// For example, to restore the value of an internal spin button widget,
    /// the function wold return an allocated string containing:
    /// <code lang="xml">
    /// <child internal-child="spinbutton">
    ///   <object class="GtkSpinButton">
    ///     <property name="value">5</property>
    ///   </object>
    /// </child>
    /// </code>
    /// </para>
    /// </remarks>
    public class Node : Box
    {
        #region GTK Constants

        /// <summary>
        /// Signal emitted when this node is clicked.
        /// </summary>
        public const string NodeFunctionClickedSignal = "node-func-clicked";

        /// <summary>
        /// Signal emitted when a socket on this node begins a drag operation.
        /// </summary>
        public const string NodeSocketDragBeginSignal = "node-drag-begin";

        /// <summary>
        /// Signal emitted when a socket on this node ends a drag operation
        /// </summary>
        public const string NodeSocketDragEndSignal = "node-drag-end";

        /// <summary>
        /// Signal emitted when a socket on this node is connected to another node.
        /// </summary>
        public const string NodeSocketConnectSignal = "node-socket-connect";

        /// <summary>
        /// Signal emitted when a socket on this node is disconnected from another node.
        /// </summary>
        public const string NodeSocketDisconnectSignal = "node-socket-disconnect";

        /// <summary>
        /// Signal emitted when a socket on this node is destroyed.
        /// </summary>
        public const string NodeSocketDestroyedSignal = "node-socket-destroyed";

        /// <summary>
        /// Property storing the X position of this node into the node view.
        /// </summary>
        public const string XProperty = "x";

        /// <summary>
        /// Property storing the Y position of this node into the node view.
        /// </summary>
        public const string YProperty = "y";

        /// <summary>
        /// Property storing the width of this node.
        /// </summary>
        public const string WidthProperty = "width";

        /// <summary>
        /// Property storing the height of this node.
        /// </summary>
        public const string HeightProperty = "height";

        /// <summary>
        /// Property storing the numeric identifier of this node.
        /// </summary>
        public const string IdProperty = "id";

        /// <summary>
        /// Property storing the label of this node.
        /// </summary>
        public const string LabelProperty = "label";

        /// <summary>
        /// Property storing the left padding value of this node.
        /// </summary>
        public const string PaddingLeftProperty = "padding-left";

        /// <summary>
        /// Property storing the right padding value of this node.
        /// </summary>
        public const string PaddingRightProperty = "padding-right";

        /// <summary>
        /// Property storing the top padding value of this node.
        /// </summary>
        public const string PaddingTopProperty = "padding-top";

        /// <summary>
        /// Property storing the bottom padding value of this node.
        /// </summary>
        public const string PaddingBottomProperty = "padding-bottom";

        /// <summary>
        /// Property storing the socket radius value of this node.
        /// </summary>
        public const string SocketRadiusProperty = "socket-radius";

        #endregion

        #region Constants

        private const int ClickedTimeout = 250;

        #endregion

        #region Fields

        private Gdk.Window _eventWindow;
        private readonly List<NodeChild> _children = new List<NodeChild>();

        private uint _id;

        private Expander _expander;
        private Button _button;

        private int _expanderSignal;
        private bool _expanderBlocked;
        private bool _lastExpanded;

        private uint _width;
        private uint _height;

        private uint _socketId;

        private Rectangle _allocation;

        private Rectangle _rectangleFunc;

        private string _iconName;

        private Border _padding;
        private Border _margin;

        private uint _activateId;

        private double _socketRadius;

        private bool _destroyed;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the radius of each socket in this node.
        /// </summary>
        /// <value>
        /// The radius of any socket in this node.
        /// </value>
        [Property(SocketRadiusProperty, "socket radius", "the radius of each socket on this node")]
        public double SocketRadius
        {
            get => _socketRadius;
            set
            {
                _socketRadius = value;

                // XXX we take the socket radius as the required (minimum) margin,
                // update it here for now
                _margin.Top = (short) value;
                _margin.Bottom = (short) value;
                _margin.Left = (short) value;
                _margin.Right = (short) value;

                foreach (NodeChild child in _children)
                    child.Socket.Radius = value;

                // The socket radius changes our visible size
                QueueAllocate();
            }
        }

        /// <summary>
        /// Gets or sets the expanded state of this node.
        /// </summary>
        /// <value>
        /// <c>true</c> if the node is expanded, <c>false</c> otherwise.
        /// </value>
        public bool Expanded
        {
            get => _expander.Expanded;
            set => _expander.Expanded = value;
        }

        /// <summary>
        /// Gets the list of input sockets.
        /// </summary>
        /// <value>
        /// A list of node sockets in sink mode.
        /// </value>
        public IReadOnlyList<NodeSocket> Sinks => _children
            .Where(c => c.Socket?.IO == NodeSocketIO.Sink)
            .Select(c => c.Socket)
            .ToList();

        /// <summary>
        /// Gets the list of output sockets.
        /// </summary>
        /// <value>
        /// A list of node sockets in source mode.
        /// </value>
        public IReadOnlyList<NodeSocket> Sources => _children
            .Where(c => c.Socket?.IO == NodeSocketIO.Source)
            .Select(c => c.Socket)
            .ToList();

        /// <summary>
        /// Gets or sets the padding of this <see cref="Node"/>.
        /// </summary>
        public Border NodePadding
        {
            get => _padding;
            set
            {
                if (_padding.Equals(value))
                    return;

                _padding = value;
                QueueAllocate();
            }
        }

        /// <summary>
        /// Gets or sets the left padding of the node.
        /// </summary>
        [Property(PaddingLeftProperty, "left padding", "the left padding of the node")]
        public short PaddingLeft
        {
            get => _padding.Left;
            set
            {
                if (_padding.Left == value)
                    return;

                _padding.Left = value;
                QueueResize();
            }
        }

        /// <summary>
        /// Gets or sets the right padding of the node.
        /// </summary>
        [Property(PaddingRightProperty, "right padding", "the right padding of the node")]
        public short PaddingRight
        {
            get => _padding.Right;
            set
            {
                if (_padding.Right == value)
                    return;

                _padding.Right = value;
                QueueResize();
            }
        }

        /// <summary>
        /// Gets or sets the top padding of the node.
        /// </summary>
        [Property(PaddingTopProperty, "top padding", "the top padding of the node")]
        public short PaddingTop
        {
            get => _padding.Top;
            set
            {
                if (_padding.Top == value)
                    return;

                _padding.Top = value;
                QueueResize();
            }
        }

        /// <summary>
        /// Gets or sets the bottom padding of the node.
        /// </summary>
        [Property(PaddingBottomProperty, "bottom padding", "the bottom padding of the node")]
        public short PaddingBottom
        {
            get => _padding.Bottom;
            set
            {
                if (_padding.Bottom == value)
                    return;

                _padding.Bottom = value;
                QueueResize();
            }
        }

        /// <summary>
        /// Gets or sets the node label.
        /// </summary>
        /// <value>
        /// The label text.
        /// </value>
        [Property(LabelProperty, "node label", "the label of this node")]
        public string Label
        {
            get => _expander.Label;
            set => _expander.Label = value;
        }

        /// <summary>
        /// The X position of the node.
        /// </summary>
        /// <value>
        /// X position of Node.
        /// </value>
        [Property(XProperty, "X position", "X position of Node")]
        public int X
        {
            get => _allocation.X;
            set
            {
                _allocation.X = value;
                QueueAllocate();
            }
        }

        /// <summary>
        /// The Y position of the node.
        /// </summary>
        /// <value>
        /// Y position of Node.
        /// </value>
        [Property(YProperty, "Y position", "Y position of Node")]
        public int Y
        {
            get => _allocation.Y;
            set
            {
                _allocation.Y = value;
                QueueAllocate();
            }
        }

        /// <summary>
        /// The width of the node.
        /// </summary>
        /// <value>
        /// Requested width of Node.
        /// </value>
        [Property(WidthProperty, "requested width", "requested width of Node")]
        public uint Width
        {
            get => _width;
            set
            {
                _width = value;
                QueueAllocate();
            }
        }

        /// <summary>
        /// The height of the node.
        /// </summary>
        /// <value>
        /// Requested height of Node.
        /// </value>
        [Property(HeightProperty, "requested height", "requested height of Node")]
        public uint Height
        {
            get => _height;
            set
            {
                _height = value;
                QueueAllocate();
            }
        }

        /// <summary>
        /// The numeric identifier of the node.
        /// </summary>
        /// <value>
        /// Numeric node identifier.
        /// </value>
        [Property(IdProperty, "numeric node identifier", "numeric node identifier")]
        public uint Id
        {
            get => _id;
            set => _id = value;
        }

        #endregion

        #region Events

        /// <summary>
        /// Event raised each times a socket on this node starts a drag operation.
        /// </summary>
        [Signal(NodeSocketDragBeginSignal)]
        public event EventHandler<NodeSocketDragEventArgs> NodeSocketDragBeginEvent;

        /// <summary>
        /// Event raised each times a socket on this node ends a drag operation.
        /// </summary>
        [Signal(NodeSocketDragEndSignal)]
        public event EventHandler NodeSocketDragEndEvent;

        /// <summary>
        /// Event raised each times a socket on this node is got connected to another.
        /// </summary>
        [Signal(NodeSocketConnectSignal)]
        public event EventHandler<NodeSocketConnectionEventArgs> NodeSocketConnectEvent;

        /// <summary>
        /// Event raised each times a socket on this node is got disconnected from another.
        /// </summary>
        [Signal(NodeSocketDisconnectSignal)]
        public event EventHandler<NodeSocketConnectionEventArgs> NodeSocketDisconnectEvent;

        /// <summary>
        /// Event raised each times a socket on this node is destroyed.
        /// </summary>
        [Signal(NodeSocketDestroyedSignal)]
        public event EventHandler<NodeSocketDestroyedEventArgs> NodeSocketDestroyedEvent;

        /// <summary>
        /// Event raised each times a socket this node is clicked.
        /// </summary>
        [Signal(NodeFunctionClickedSignal)]
        public event EventHandler NodeFunctionClickedEvent;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new <see cref="Node"/>.
        /// </summary>
        public Node(IntPtr raw)
            : base(raw)
        {
            Init();
        }

        /// <summary>
        /// Creates a new <see cref="Node"/>.
        /// </summary>
        public Node()
            : base(Orientation.Vertical, 4)
        {
            Init();
        }

        #endregion

        #region Methods

        private void Init()
        {
            // XXX: Set some defaults here, some are not properly configurable yet
            _rectangleFunc = new Rectangle
            {
                Width = 20,
                Height = 20,
            };

            _width = 100;
            _height = 100;

            _socketRadius = 8.0;

            _padding = new Border
            {
                Top = 10,
                Bottom = 10,
                Left = 10,
                Right = 10,
            };

            _margin = new Border
            {
                Top = (short) _socketRadius,
                Bottom = (short) _socketRadius,
                Left = (short) _socketRadius,
                Right = (short) _socketRadius,
            };

            _iconName = "edit-delete-symbolic";

            Homogeneous = false;

            // XXX: Ensure this once on init, if the user decides to change this later,
            // it's not our problem
            // TODO: Make whole thing orientable (maybe)
            Orientation = Orientation.Vertical;

            // Add an expander for minimization of the node, and a descriptive label
            _expander = new Expander("Node")
            {
                Expanded = true
            };
            PackStart(_expander, false, false, 0);

            _expander.AddNotification("expanded", NodeExpanderHandler);

            _expanderBlocked = false;
            _lastExpanded = true;

            HasWindow = false;
        }

        private void SocketDragBeginHandler(object sender, DragBeginArgs args)
        {
            Rectangle allocSocket = ((NodeSocket) sender).Allocation;
            Rectangle allocNode = Allocation;

            OnNodeSocketBeginDragged
            (
                allocNode.X + allocSocket.X + allocSocket.Width / 2,
                allocNode.Y + allocSocket.Y + allocSocket.Height / 2
            );

            args.RetVal = false;
        }

        private void SocketDragEndHandler(object sender, DragEndArgs args)
        {
            OnNodeSocketEndDragged();
        }

        private void SocketConnectHandler(object sender, SocketEventArgs args)
        {
            OnNodeSocketConnect((NodeSocket) sender, args.Socket);
        }

        private void SocketDisconnectHandler(object sender, SocketEventArgs args)
        {
            OnNodeSocketDisconnect((NodeSocket) sender, args.Socket);
        }

        private void SocketDestroyedHandler(object sender, EventArgs args)
        {
            OnNodeSocketDestroyed((NodeSocket) sender);
        }

        private void NodeExpanderHandler(object sender, NotifyArgs args)
        {
            // @na2axl: Workaround, since GtkSharp doesn't have support
            // to block signals
            if (_expanderBlocked)
                return;

            foreach (NodeChild child in _children)
                child.Child.Visible = _expander.Expanded;

            // If the user set a center widget, make sure to hide it too, as
            // we don't track it as one of the items in the node.
            // We could actually use the center widget to display a persistent
            // widget which is shown even when the expander is collapsed, but this
            // could open another can of worms of allocation management.
            // Let's keep it simple for now.
            Widget center = CenterWidget;
            if (center != null)
                center.Visible = _expander.Expanded;

            Parent.QueueDraw();
        }

        private NodeSocket ItemAddReal(Widget child, NodeSocketIO io, uint key = 0)
        {
            if (child.Parent != null)
                return null;

            NodeChild childInfo = new NodeChild(this, child);

            switch (io)
            {
                case NodeSocketIO.Source:
                {
                    RGBA rgba = new RGBA
                    {
                        Red = 0.0,
                        Green = 0.38,
                        Blue = 0.65,
                        Alpha = 1.0,
                    };

                    childInfo.Socket = new NodeSocket(NodeSocketIO.Source)
                    {
                        RGBA = rgba,
                    };

                    break;
                }

                case NodeSocketIO.Sink:
                {
                    RGBA rgba = new RGBA
                    {
                        Red = 0.92,
                        Green = 0.67,
                        Blue = 0.0,
                        Alpha = 1.0,
                    };

                    childInfo.Socket = new NodeSocket(NodeSocketIO.Sink)
                    {
                        RGBA = rgba,
                    };

                    break;
                }

                default:
                    childInfo.Socket = new NodeSocket();
                    break;
            }

            // We set an incremental socket id here, so a node item can later be
            // identified for restarting socket connections when loading from
            // XML via NodeView
            childInfo.Socket.Id = _socketId++;

            childInfo.Socket.IO = io;
            childInfo.Socket.Key = key;

            childInfo.Socket.Radius = _socketRadius;

            // We need to collect all signals emitted by the sockets associated with
            // our node items, so we can pass them on the next layer
            childInfo.Socket.DragBegin += SocketDragBeginHandler;
            childInfo.Socket.DragEnd += SocketDragEndHandler;
            childInfo.Socket.SocketConnectEvent += SocketConnectHandler;
            childInfo.Socket.SocketDisconnectEvent += SocketDisconnectHandler;
            childInfo.Socket.SocketDestroyedEvent += SocketDestroyedHandler;

            _children.Add(childInfo);

            PackStart(childInfo.Child, false, false, 0);

            childInfo.Socket.Parent = this;
            childInfo.Socket.Visible = true;

            if (_eventWindow != null)
            {
                childInfo.SetParentWindow(_eventWindow);
            }

            return childInfo.Socket;
        }

        private StyleContext GetStyle()
        {
            // All we really want is to draw a frame, so we'll take our
            // style context from a button

            Widget b = new Button();
            StyleContext c = b.StyleContext;

            // TODO
            return c;
        }

        private void DrawFrame(Cairo.Context cr, in Rectangle allocation)
        {
            StyleContext c = GetStyle();

            // Draw a representation of the node
            c.Save();
            c.RenderBackground(cr, allocation.X, allocation.Y, allocation.Width, allocation.Height);
            c.RenderFrame(cr, allocation.X, allocation.Y, allocation.Width, allocation.Height);
            c.Restore();

            // Set functional icon allocation for clicks
            _rectangleFunc.X = allocation.X + allocation.Width - 25; // XXX
            _rectangleFunc.Y = allocation.Y + _padding.Top;

            if (!string.IsNullOrEmpty(_iconName))
            {
                IconTheme it = IconTheme.Default;
                Pixbuf pb = it.LoadIcon(_iconName, _rectangleFunc.Height, 0);

                cr.Save();
                Gdk.CairoHelper.SetSourcePixbuf(cr, pb, _rectangleFunc.X, _rectangleFunc.Y);

                cr.Paint();
                cr.Restore();
            }

            c.Dispose();
        }

        private void SizeAllocateSocket(NodeChild child, in Rectangle allocation)
        {
            Rectangle allocSocket = new Rectangle();
            NodeSocketIO mode = child.Socket.IO;

            if (mode != NodeSocketIO.Source && mode != NodeSocketIO.Sink)
                return;

            child.Socket.GetPreferredHeight(out int minimum, out int natural);
            allocSocket.Height = Math.Min(minimum, natural);

            allocSocket.Y = allocation.Y;

            if (_expander.Expanded)
                allocSocket.Y += (allocation.Height - allocSocket.Height) / 2 - _allocation.Y;

            child.Socket.GetPreferredWidth(out minimum, out natural);
            allocSocket.Width = Math.Min(minimum, natural);

            allocSocket.X = -allocSocket.Width / 2 + _margin.Left;

            if (mode == NodeSocketIO.Source)
                allocSocket.X += _allocation.Width - _margin.Right - _margin.Left;

            child.Socket.SizeAllocate(allocSocket);
        }

        private void SizeAllocateVisibleChildSockets()
        {
            foreach (NodeChild child in _children)
            {
                if (!child.Child.IsVisible)
                    continue;

                Rectangle alloc = child.Child.Allocation;

                // Sockets are not drawn within the context of the box, so their
                // Origin is relative to the parent of the node
                alloc.Y += _allocation.Y;

                SizeAllocateSocket(child, in alloc);
            }
        }

        private void GetVisibleSocketStack(NodeSocketIO mode, out int sockets, out int height)
        {
            int h = 0;
            int n = 0;

            foreach (NodeChild child in _children)
            {
                NodeSocketIO io = child.Socket.IO;

                if (io != mode)
                    continue;

                if (!child.Socket.IsVisible)
                    continue;

                child.Socket.GetPreferredHeight(out int minimum, out int natural);

                h += Math.Min(minimum, natural);
                n++;
            }

            sockets = n;
            height = h;
        }

        private void DistributeVisibleSocketStack(Rectangle allocation, NodeSocketIO mode, int sockets, int height)
        {
            double y;
            double step = 0.0;

            // Prevent div/0
            sockets = Math.Min(height, sockets);

            // Not doing this in a floating point representation can cause noticeable
            // rounding errors depending on the socket radius and number of sockets

            if (sockets > 1)
            {
                step = (double) height / (sockets - 1);
                y = allocation.Y;
            }
            else
            {
                y = (double) height / 2;
            }

            foreach (NodeChild child in _children)
            {
                NodeSocketIO io = child.Socket.IO;

                if (io != mode)
                    continue;

                if (!child.Socket.IsVisible)
                    continue;

                SizeAllocateSocket(child, allocation);

                y += step;
                allocation.Y = (int) y;
            }
        }

        private void SizeAllocateVisibleSockets(ref Rectangle allocation)
        {
            GetVisibleSocketStack(NodeSocketIO.Source, out int sources, out int sourceHeight);
            GetVisibleSocketStack(NodeSocketIO.Sink, out int sinks, out int sinkHeight);

            int height = Math.Max(sourceHeight, sinkHeight);

            // Adjust for expander/label
            Rectangle allExp = _expander.Allocation;

            if (height < allExp.Height)
                height += _padding.Top + _padding.Bottom;

            DistributeVisibleSocketStack(allocation, NodeSocketIO.Source, sources, height);
            DistributeVisibleSocketStack(allocation, NodeSocketIO.Sink, sinks, height);

            // Our sockets are round and stick out the top and bottom of the frame
            // by one radius
            allocation.Height = (int) (height + 2 * _socketRadius);
        }

        private bool ClickedTimeoutHandler()
        {
            // Just in case we are destroyed while timeout was running
            if (_destroyed)
                return false;

            if (_activateId != 0)
                Source.Remove(_activateId);

            _activateId = 0;

            return false;
        }

        private NodeChild GetChild(Widget widget)
        {
            foreach (NodeChild child in from NodeChild child in _children
                                        where child.Child == widget
                                        select child)
            {
                return child;
            }

            Trace.WriteLine("Widget is not a child of Node");

            return null;
        }

        protected internal virtual void OnInitChildren()
        { }

        protected virtual void OnNodeSocketBeginDragged(int x, int y)
        {
            NodeSocketDragBeginEvent?.Invoke(this, new NodeSocketDragEventArgs(x, y));
        }

        protected virtual void OnNodeSocketEndDragged()
        {
            NodeSocketDragEndEvent?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnNodeSocketConnect(NodeSocket sink, NodeSocket source)
        {
            NodeSocketConnectEvent?.Invoke(this, new NodeSocketConnectionEventArgs(sink, source));
        }

        protected virtual void OnNodeSocketDisconnect(NodeSocket sink, NodeSocket source)
        {
            NodeSocketDisconnectEvent?.Invoke(this, new NodeSocketConnectionEventArgs(sink, source));
        }

        protected virtual void OnNodeSocketDestroyed(NodeSocket socket)
        {
            NodeSocketDestroyedEvent?.Invoke(this, new NodeSocketDestroyedEventArgs(socket));
        }

        protected virtual void OnNodeFunctionClicked()
        {
            NodeFunctionClickedEvent?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The expander is very very nasty in that it responds to an isolated
        /// "release" event, which I'm apparently unable to block.
        /// If the node is contained in a <see cref="NodeView"/>, it will therefore
        /// expand/contract if someone clicks the label when executing a node "drag"
        /// motion.
        /// </summary>
        /// <remarks>
        /// Calling this method repeatedly will affect the state only once.
        /// </remarks>
        public void BlockExpander()
        {
            if (_expanderBlocked)
                return;

            _expanderBlocked = true;
            _lastExpanded = Expanded;
        }

        /// <summary>
        /// Unblocks the expander from receiving signals.
        /// </summary>
        public void UnblockExpander()
        {
            if (!_expanderBlocked)
                return;

            _expanderBlocked = false;
            Expanded = _lastExpanded;
        }

        /// <summary>
        /// Sets the icon name.
        /// </summary>
        /// <param name="iconName">The name of the icon to be displayed.</param>
        public void SetIconName(string iconName)
        {
            _iconName = null;

            if (!string.IsNullOrEmpty(iconName))
                _iconName = iconName;
        }

        /// <summary>
        /// This returns an XML description of the internal state configuration,
        /// so it can be restored with <see cref="Builder"/>.
        /// </summary>
        /// <returns>
        /// An XML string describing the internal configuration; may be <c>null</c>.
        /// </returns>
        public virtual XmlNode[] ExportProperties()
        {
            return null;
        }

        /// <summary>
        /// Adds an item to this node.
        /// </summary>
        /// <param name="widget">The item widget to add.</param>
        /// <param name="mode">The mode of the generated socket for this item.</param>
        /// <param name="key">
        /// The key of the generated socket for this item.
        /// Defaults to 0, means the generated socket is compatible with any other socket.
        /// </param>
        /// <returns>
        /// The generated socket widget for the added item.
        /// </returns>
        public NodeSocket ItemAdd(Widget widget, NodeSocketIO mode, uint key = 0)
        {
            return ItemAddReal(widget, mode, key);
        }

        #endregion

        #region Widget Implementation

        protected override void OnMapped()
        {
            base.OnMapped();

            _eventWindow?.Show();
        }

        protected override void OnUnmapped()
        {
            _eventWindow?.Hide();

            base.OnUnmapped();
        }

        protected override void OnRealized()
        {
            base.OnRealized();

            IsRealized = true;
            Rectangle allocation = Allocation;

            Gdk.Window window = ParentWindow;
            Window = window;

            WindowAttr attributes = new WindowAttr
            {
                WindowType = Gdk.WindowType.Child,
                X = allocation.X,
                Y = allocation.Y,
                Width = allocation.Width,
                Height = allocation.Height,
                Visual = Visual,
                Wclass = WindowWindowClass.InputOutput,
                EventMask = (int) (Events
                    | EventMask.ButtonPressMask
                    | EventMask.ButtonReleaseMask
                    | EventMask.PointerMotionMask
                    | EventMask.TouchMask
                    | EventMask.EnterNotifyMask
                    | EventMask.LeaveNotifyMask)
            };

            const int attributesMask = (int) (WindowAttributesType.X | WindowAttributesType.Y);

            _eventWindow = new Gdk.Window(window, attributes, attributesMask);
            RegisterWindow(_eventWindow);

            foreach (NodeChild child in _children)
                child.SetParentWindow(_eventWindow);

            _expander.ParentWindow = _eventWindow;
        }

        protected override void OnUnrealized()
        {
            if (_eventWindow != null)
            {
                UnregisterWindow(_eventWindow);
                _eventWindow.Destroy();
                _eventWindow = null;
            }

            if (_activateId != 0)
            {
                Source.Remove(_activateId);
                _activateId = 0;
            }

            base.OnUnrealized();
        }

        protected override void OnAdjustSizeRequest(Orientation orientation, out int minimum_size, out int natural_size)
        {
            int h = _padding.Left + _padding.Right
                  + _margin.Left + _margin.Right;

            int v = _padding.Top + _padding.Bottom
                  + _margin.Top + _margin.Bottom;

            if (orientation == Orientation.Horizontal)
            {
                // Adjust extra pixel size of "func" button as well
                minimum_size = h + 25; // XXX
                natural_size = h + 25;
            }
            else
            {
                minimum_size = v;
                natural_size = v;
            }
        }

        protected override void OnSizeAllocated(Rectangle allocation)
        {
            int top, left, right, bottom;

            _allocation.X = allocation.X;
            _allocation.Y = allocation.Y;
            _allocation.Width = allocation.Width;
            _allocation.Height = allocation.Height;

            top = _padding.Top + _margin.Top;
            left = _padding.Left + _margin.Left;
            right = _padding.Right + _margin.Right;
            bottom = _padding.Bottom + _margin.Bottom;

            allocation.X = left;
            allocation.Y = right;
            allocation.Width -= left + right;
            allocation.Height -= top + bottom;

            // Chain up to allocate the node items
            base.OnSizeAllocated(allocation);

            if (!_expander.Expanded)
            {
                // Adjust for expander/label first, it may have gotten too mush space
                // allocated, we want to go as compact as possible
                Rectangle alloc = _expander.Allocation;
                _expander.GetPreferredWidth(out int minimum, out int natural);
                alloc.Width = Math.Min(minimum, natural);
                _expander.SetAllocation(alloc);

                // Now adapt the allocation
                _allocation.Width = alloc.Width + left + right + 25; // XXX

                alloc.Y = 0;
                SizeAllocateVisibleSockets(ref alloc);

                // Update height from socket placement
                _allocation.Height = alloc.Height;
            }
            else
            {
                SizeAllocateVisibleChildSockets();
            }

            SetAllocation(_allocation);

            if (!IsRealized)
                return;

            if (_eventWindow == null)
                return;

            _eventWindow.MoveResize(_allocation.X, _allocation.Y, _allocation.Width, _allocation.Height);
        }

        protected override bool OnDrawn(Cairo.Context cr)
        {
            Rectangle allocation = Allocation;

            allocation.X = _margin.Left;
            allocation.Y = _margin.Top;
            allocation.Width -= _margin.Left + _margin.Right;
            allocation.Height -= _margin.Top + _margin.Bottom;

            DrawFrame(cr, allocation);

            base.OnDrawn(cr);

            return false;
        }

        protected override bool OnButtonPressEvent(EventButton evnt)
        {
            if (!_rectangleFunc.Contains((int) evnt.X, (int) evnt.Y))
                return false;

            _activateId = Threads.AddTimeout((int) Priority.Default, ClickedTimeout, ClickedTimeoutHandler);

            return true;
        }

        protected override bool OnButtonReleaseEvent(EventButton evnt)
        {
            if (!_rectangleFunc.Contains((int) evnt.X, (int) evnt.Y))
                return false;

            if (_activateId != 0)
                OnNodeFunctionClicked();

            return true;
        }

        protected override void OnDestroyed()
        {
            base.OnDestroyed();
            _destroyed = true;
        }

        #endregion

        #region Container Implementation

        protected override void OnAdded(Widget widget)
        {
            ItemAddReal(widget, NodeSocketIO.Disable);
        }

        protected override void OnRemoved(Widget widget)
        {
            foreach (NodeChild child in _children.ToArray())
            {
                if (child.Child != widget)
                    continue;

                child.Socket.Unparent();
                base.OnRemoved(widget);

                _children.Remove(child);
                child.Dispose();

                return;
            }
        }

        protected override void ForAll(bool include_internals, Callback callback)
        {
            base.ForAll(include_internals, callback);

            if (!include_internals)
                return;

            foreach (NodeChild child in _children)
            {
                callback.Invoke(child.Socket);
            }
        }

        protected override void OnSetChildProperty(Widget child, uint property_id, Value value, IntPtr pspec)
        {
            NodeChild nodeChild = GetChild(child);

            if (nodeChild == null)
                return;

            // This is kinda stupid, but at the moment I don't see how I can save an
            // XML description of the node view's contents and restore a (partial)
            // configuration with Gtk.Builder all without tracking the child properties
            // within the node children as well
            switch (property_id)
            {
                case NodeChild.ChildPropertySocketId:
                {
                    nodeChild.Socket.Id = (uint) value.Val;
                    break;
                }

                case NodeChild.ChildPropertyInputId:
                {
                    nodeChild.InputId = (uint) value.Val;
                    break;
                }

                case NodeChild.ChildPropertyIOMode:
                {
                    nodeChild.Socket.IO = (NodeSocketIO) value.Val;
                    break;
                }

                default:
                    throw new InvalidOperationException("Invalid property ID");
            }
        }

        protected override void OnGetChildProperty(Widget child, uint property_id, Value value, IntPtr pspec)
        {
            NodeChild nodeChild = GetChild(child);

            if (nodeChild == null)
                return;

            switch (property_id)
            {
                case NodeChild.ChildPropertySocketId:
                {
                    value.Val = nodeChild.Socket.Id;
                    break;
                }

                case NodeChild.ChildPropertyInputId:
                {
                    value.Val = nodeChild.InputId;
                    break;
                }

                case NodeChild.ChildPropertyIOMode:
                {
                    value.Val = nodeChild.Socket.IO;
                    break;
                }

                default:
                    throw new InvalidOperationException("Invalid property ID");
            }
        }

        #endregion

        #region Node Child

        private class NodeChild : ContainerChild, IDisposable
        {
            #region Constants

            internal const uint ChildPropertySocketId = 1;
            internal const uint ChildPropertyInputId = 2;
            internal const uint ChildPropertyIOMode = 3;

            #endregion

            #region Properties

            public NodeSocket Socket { get; set; }

            [ChildProperty("socketid")]
            public uint SocketId
            {
                get => Socket.Id;
                set => Socket.Id = value;
            }

            [ChildProperty("inputid")]
            public uint InputId { get; set; }

            [ChildProperty("Mode")]
            public NodeSocketIO IOMode
            {
                get => Socket.IO;
                set => Socket.IO = value;
            }

            #endregion

            #region Constructors

            public NodeChild(Container parent, Widget child)
                : base(parent, child)
            { }

            #endregion

            #region Methods

            internal void SetParentWindow(Gdk.Window window)
            {
                Child.ParentWindow = window;
                Socket.ParentWindow = window;
            }

            #endregion

            #region IDisposable Implementation

            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                Child.Dispose();
                Socket.Dispose();

                _disposed = true;
            }

            #endregion
        }

        #endregion
    }
}
