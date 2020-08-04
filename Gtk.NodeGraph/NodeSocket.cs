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
using System.Collections;
using System.Diagnostics;
using Cairo;
using Gdk;
using GLib;

namespace Gtk.NodeGraph
{
    /// <summary>
    /// Event arguments for <see cref="NodeSocket.SocketConnectEvent"/>, <see cref="NodeSocket.SocketDisconnectEvent"/>,
    /// and <see cref="NodeSocket.SocketKeyChangeEvent"/>.
    /// </summary>
    public class SocketEventArgs : EventArgs
    {
        public NodeSocket Socket { get; }

        internal SocketEventArgs(NodeSocket socket)
        {
            Socket = socket;
        }
    }

    /// <summary>
    /// Event arguments for <see cref="NodeSocket.SocketDataIncomingEvent"/> and
    /// <see cref="NodeSocket.SocketDataOutgoingEvent"/>.
    /// </summary>
    public class SocketDataEventArgs : EventArgs
    {
        public object Data { get; }

        internal SocketDataEventArgs(object data)
        {
            Data = data;
        }
    }

    /// <summary>
    /// <para>
    /// The <see cref="NodeSocket"/> is a widget, serving as an IO transporter to other
    /// <see cref="NodeSocket"/>s. The user can set one of three IO nodes: <see cref="NodeSocketIO.Sink"/>,
    /// <see cref="NodeSocketIO.Source"/> and <see cref="NodeSocketIO.Disable"/>.
    /// A socket in sink mode will accept only a single input source,
    /// a socket in source mode will provide output to any connected sink.
    /// Connections a re established by drag-and-drop action of a source to a sink
    /// by the user. A sink socket will emit the :socket-connect signal when a
    /// connection is established. If the user initiates a drag on a sink which is
    /// already connected to a source, the sink will disconnect
    /// from that source and the drag envent will redirect to the source
    /// socket.
    /// </para>
    /// <para>
    /// In order to identify compatible sockets, a <see cref="Key"/> can be provided by the user.
    /// The source key is transported to the sink in the drag data exchange and the sink will
    /// reject the connection if the key does match. This mechanism ensures that only interpretable
    /// data is received on the sink input.
    /// </para>
    /// <para>
    /// A key value of 0 is special in that any input will be accepted.
    /// </para>
    /// <para>
    /// If the user changes the key, the socket will emit the :socket-disconnect signal to notify any
    /// connected sinks or sources, so they can initiate a disconnect, if their keys does not match or
    /// is different from 0.
    /// </para>
    /// <para>
    /// Connections on sinks are established by connecting to the :socket-outgoing signal of the source.
    /// This means that the source is not aware of the number of connected sinks, as all data is
    /// transported by the GType signal system.
    /// </para>
    /// <para>
    /// The user can push output from a source by calling <see cref="Write"/> on the socket. To get data
    /// received by a sink, the user must connect to the :socket-incoming signal.
    /// </para>
    /// <para>
    /// If a socket is destroyed or disconnects from a source, it will emit the :socket-destroyed and
    /// :socket-disconnected signals respectively.
    /// </para>
    /// <para>
    /// Any socket attached to the destroyed socket will initiate a disconnect.
    /// </para>
    /// </summary>
    public class NodeSocket : Widget
    {
        #region GTK Constants

        /// <summary>
        /// Signal emitted when this socket get connected to another one.
        /// </summary>
        public const string SocketConnectSignal = "socket-connect";

        /// <summary>
        /// Signal emitted when this socket get disconnected from another one.
        /// </summary>
        public const string SocketDisconnectSignal = "socket-disconnect";

        /// <summary>
        /// Signal emitted when the key value of this socket was changed.
        /// </summary>
        public const string SocketKeyChangeSignal = "socket-key-change";

        /// <summary>
        /// Signal emitted when this socket receive data form another one.
        /// </summary>
        public const string SocketDataIncomingSignal = "socket-incoming";

        /// <summary>
        /// Signal emitted when this socket is sending data to connected sockets.
        /// </summary>
        public const string SocketDataOutgoingSignal = "socket-outgoing";

        /// <summary>
        /// Signal emitted when this socket is destroyed.
        /// </summary>
        public const string SocketDestroyedSignal = "socket-destroyed";

        /// <summary>
        /// Property storing the RGBA color of the socket.
        /// </summary>
        public const string RGBAProperty = "rgba";

        /// <summary>
        /// Property storing the radius of the socket.
        /// </summary>
        public const string RadiusProperty = "radius";

        /// <summary>
        /// Property storing the socket type.
        /// </summary>
        public const string IOProperty = "io";

        /// <summary>
        /// Property storing the socket compatibility key.
        /// </summary>
        public const string KeyProperty = "key";

        /// <summary>
        /// Property storing the socket id.
        /// </summary>
        public const string IdProperty = "id";

        #endregion

        #region Fields

        private static readonly TargetEntry[] DropTypes = new[]
        {
            new TargetEntry("gtk_nodesocket", TargetFlags.App, 0)
        };

        private static readonly Hashtable SocketDragDataCache = new Hashtable();

        private Gdk.Window _eventWindow;

        private NodeSocketIO _io;
        private uint _id;
        private uint _key;
        private RGBA _rgba;
        private double _radius;

        private NodeSocket _input;

        private bool _inNodeSocket;

        #endregion

        #region Properties

        /// <summary>
        /// The key of the source socket connected to this one.
        /// </summary>
        /// <remarks>
        /// Querying this value only have sense when this socket have the type
        /// <see cref="NodeSocketIO.Sink"/>. Otherwise, the returned value will
        /// always be 0.
        /// </remarks>
        /// <value>
        /// Socket compatibility key.
        /// </value>
        public uint RemoteKey => _input != null ? _input.Key : 0;

        /// <summary>
        /// Returns the input <see cref="NodeSocket"/> for this socket, or
        /// <c>null</c> if no input is connected or the socket is not is sink mode.
        /// </summary>
        public NodeSocket Input => _input;

        /// <summary>
        /// The RGBA color of the socket.
        /// </summary>
        /// <value>
        /// Current RGBA color.
        /// </value>
        [Property(RGBAProperty, "Current RGBA Color", "The RGBA color of the socket")]
        public RGBA RGBA
        {
            get => _rgba;
            set
            {
                _rgba = value;
                Notify(RGBAProperty);

                QueueDraw();
            }
        }

        /// <summary>
        /// The radius of the socket.
        /// </summary>
        /// <value>
        /// Current socket radius.
        /// </value>
        [Property(RadiusProperty, "Current Socket Radius", "The radius of the socket")]
        public double Radius
        {
            get => _radius;
            set
            {
                _radius = value;
                Notify(RadiusProperty);

                QueueResize();
            }
        }

        /// <summary>
        /// The configured socket type, either input or output.
        /// </summary>
        /// <value>
        /// The configured socket type.
        /// </value>
        [Property(IOProperty, "Socket I/O Type", "The configured socket type, either input or output")]
        public NodeSocketIO IO
        {
            get => _io;
            set
            {
                if (value == _io)
                    return;

                _io = value;

                // If there is an input source, disconnect it
                Disconnect();

                // Notify any sinks so they can disconnect
                OnSocketDisconnected(null);

                if (value == NodeSocketIO.Source)
                {
                    Drag.SourceSet(this, ModifierType.Button1Mask | ModifierType.Button3Mask, DropTypes, DragAction.Copy);
                    Drag.DestUnset(this);
                }

                if (value == NodeSocketIO.Sink)
                {
                    Drag.SourceUnset(this);
                    Drag.DestSet(this, DestDefaults.Motion | DestDefaults.Highlight | DestDefaults.Drop, DropTypes, DragAction.Copy);
                    Drag.DestSetTrackMotion(this, true);
                }

                QueueAllocate();
            }
        }

        /// <summary>
        /// The socket compatibility key.
        /// </summary>
        /// <value>
        /// Socket compatibility key.
        /// </value>
        [Property(KeyProperty, "Socket Compatibility Key", "The socket compatibility key")]
        public uint Key
        {
            get => _key;
            set
            {
                if (_key == value)
                    return;

                _key = value;

                if (_key == 0)
                    return;

                // The key is not 0; If there is a non-matching input source, disconnect it
                if (_key != RemoteKey)
                    Disconnect();

                // Notify any sinks so they can disconnect
                OnSocketKeyChanged(this);
            }
        }

        /// <summary>
        /// The socket numeric identifier.
        /// </summary>
        /// <value>
        /// Socket numeric identifier.
        /// </value>
        [Property(IdProperty, "Socket Numeric Identifier", "The socket numeric identifier")]
        public uint Id
        {
            get => _id;
            set
            {
                if (value != 0)
                    _id = value;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Event raised each times this socket is got connected to another.
        /// </summary>
        [Signal(SocketConnectSignal)]
        public event EventHandler<SocketEventArgs> SocketConnectEvent;

        /// <summary>
        /// Event raised each times this socket got disconnected from another.
        /// </summary>
        [Signal(SocketDisconnectSignal)]
        public event EventHandler<SocketEventArgs> SocketDisconnectEvent;

        /// <summary>
        /// Event raised each times this socket changes his key.
        /// </summary>
        [Signal(SocketKeyChangeSignal)]
        public event EventHandler<SocketEventArgs> SocketKeyChangeEvent;

        /// <summary>
        /// Event raised each times this socket receives data form another.
        /// </summary>
        [Signal(SocketDataIncomingSignal)]
        public event EventHandler<SocketDataEventArgs> SocketDataIncomingEvent;

        /// <summary>
        /// Event raised each times this socket send data to another.
        /// </summary>
        [Signal(SocketDataOutgoingSignal)]
        public event EventHandler<SocketDataEventArgs> SocketDataOutgoingEvent;

        /// <summary>
        /// Event raised when this socket is destroyed.
        /// </summary>
        [Signal(SocketDestroyedSignal)]
        public event EventHandler SocketDestroyedEvent;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new node socket.
        /// </summary>
        public NodeSocket()
        {
            CanFocus = true;
            ReceivesDefault = true;
            HasWindow = false;

            _rgba = new RGBA
            {
                Red = 1.0,
                Green = 1.0,
                Blue = 1.0,
                Alpha = 1.0,
            };

            _radius = 8.0;

            _key = 0; // Accept any connection by default

            _inNodeSocket = false;
        }

        /// <summary>
        /// Creates a new node socket in the given <paramref name="io"/> mode.
        /// </summary>
        /// <param name="io">The IO mode to configure.</param>
        public NodeSocket(NodeSocketIO io)
            : this()
        {
            IO = io;
        }

        #endregion

        #region Methods

        private void DataIncomingSignalHandler(object sender, SocketDataEventArgs args)
        {
            Write(args.Data);
        }

        private void DisconnectSignalHandler(object sender, SocketEventArgs args)
        {
            Disconnect();
        }

        private void KeyChangeSignalHandler(object sender, SocketEventArgs args)
        {
            // We disconnect if our key does not match
            if (_key != RemoteKey)
                Disconnect();
        }

        private void DestroyedSignalHandler(object sender, EventArgs args)
        {
            // Technically, this is not necessary right now, because our signal
            // callbacks will be disconnect on destruction of a source anyways, but
            // we may want to do other things in the future, so we'll handle that signal
            // always
            Disconnect();
        }

        private void SetDragIcon(DragContext context)
        {
            Surface surface = new ImageSurface(Format.Argb32, (int) (2.0 * _radius), (int) (2.0 * _radius));
            Context cr = new Context(surface);

            cr.SetSourceRGBA(_rgba.Red, _rgba.Green, _rgba.Blue, _rgba.Alpha);
            cr.Arc(_radius, _radius, _radius, 0.0, 2.0 * Math.PI);
            cr.Fill();

            Drag.SetIconSurface(context, surface);

            cr.Dispose();
            surface.Dispose();
        }

        private void DragSourceRedirect()
        {
            if (_input == null)
                return;

            _input.SocketDataOutgoingEvent -= DataIncomingSignalHandler;
            _input.SocketDisconnectEvent -= DisconnectSignalHandler;
            _input.SocketKeyChangeEvent -= KeyChangeSignalHandler;
            _input.SocketDestroyedEvent -= DestroyedSignalHandler;

            OnSocketDisconnected(_input);

            _input = null;

            // Remove as drag source
            if (_io == NodeSocketIO.Sink)
                Drag.SourceUnset(this);

            // Begin drag on previous source, so user can redirect connection
            Drag.BeginWithCoordinates
            (
                _input,
                new TargetList(DropTypes),
                DragAction.Copy,
                (int) (ModifierType.Button1Mask | ModifierType.Button3Mask),
                null,
                -1,
                -1
            );
        }

        private void ConnectSocketsInternal(NodeSocket source)
        {
            if (source._io != NodeSocketIO.Source)
            {
                Trace.WriteLine($"Node Socket {source._id} not in source mode.");
                return;
            }

            if (_io != NodeSocketIO.Sink)
            {
                Trace.WriteLine($"Node Socket {_id} not in sink mode.");
                return;
            }

            if (_key != 0 && source._key != _key)
            {
                Trace.WriteLine("Node Socket keys incompatible, source rejected.");
                return;
            }

            // If there is an input source, disconnect it
            Disconnect();

            _input = source;

            _input.SocketDataOutgoingEvent += DataIncomingSignalHandler;
            _input.SocketDisconnectEvent += DisconnectSignalHandler;
            _input.SocketKeyChangeEvent += KeyChangeSignalHandler;
            _input.SocketDestroyedEvent += DestroyedSignalHandler;

            // Become a drag source, so the user can disconnect from the sink
            Drag.SourceSet(this, ModifierType.Button1Mask | ModifierType.Button3Mask, DropTypes, DragAction.Copy);

            // Emit notification of connection from sink
            OnSocketConnected(source);

            // Emit notification of connection from source
            source.OnSocketConnected(source);
        }

        protected virtual void OnSocketKeyChanged(NodeSocket source)
        {
            SocketKeyChangeEvent?.Invoke(this, new SocketEventArgs(source));
        }

        protected virtual void OnSocketConnected(NodeSocket source)
        {
            SocketConnectEvent?.Invoke(this, new SocketEventArgs(source));
        }

        protected virtual void OnSocketDataIncoming(object payload)
        {
            SocketDataIncomingEvent?.Invoke(this, new SocketDataEventArgs(payload));
        }

        protected virtual void OnSocketDataOutgoing(object payload)
        {
            SocketDataOutgoingEvent?.Invoke(this, new SocketDataEventArgs(payload));
        }

        protected virtual void OnSocketDisconnected(NodeSocket source)
        {
            SocketDisconnectEvent?.Invoke(this, new SocketEventArgs(source));
        }

        protected virtual void OnSocketDestroyed()
        {
            SocketDestroyedEvent?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Widget Implementation

        protected override void OnDragBegin(DragContext context)
        {
            // We apparently have to set it...
            SetDragIcon(context);

            // ...to hide it
            context.DragWindow.Hide();

            base.OnDragBegin(context);

            // If we're connected, disconnect here and abort the drag and reroute
            // it to our original source socket
            DragSourceRedirect();
        }

        protected override bool OnDragMotion(DragContext context, int x, int y, uint time)
        {
            return false;
        }

        protected override void OnDragDataReceived(DragContext context, int x, int y, SelectionData selection_data, uint info, uint time)
        {
            string key = System.Text.Encoding.ASCII.GetString(selection_data.Data);
            if (!SocketDragDataCache.ContainsKey(key))
                return;

            // We want the source socket so we want to connect to our input
            if (!(SocketDragDataCache[key] is NodeSocket source))
                return;

            SocketDragDataCache.Remove(key);
            ConnectSocketsInternal(source);
        }

        protected override void OnDragDataGet(DragContext context, SelectionData selection_data, uint info, uint time)
        {
            string key = Guid.NewGuid().ToString();
            SocketDragDataCache.Add(key, this);
            byte[] data = System.Text.Encoding.ASCII.GetBytes(key);
            selection_data.Set(selection_data.Target, 32, data, data.Length);
        }

        protected override bool OnDragFailed(DragContext context, DragResult result)
        {
            // Do not show drag cancel animation
            return true;
        }

        protected override void OnDragEnd(DragContext context)
        {
            if (_io != NodeSocketIO.Sink)
                base.OnDragEnd(context);
        }

        protected override void OnDestroyed()
        {
            OnSocketDestroyed();

            // Disconnect any inputs
            Disconnect();

            base.OnDestroyed();
        }

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
            IsRealized = true;
            Gdk.Rectangle allocation = Allocation;

            Gdk.Window window = ParentWindow;
            Window = window;

            // Event window of size of circle
            WindowAttr attributes = new WindowAttr
            {
                WindowType = Gdk.WindowType.Child,
                X = allocation.X,
                Y = allocation.Y,
                Width = (int) Math.Ceiling(2.0 * _radius),
                Height = (int) Math.Ceiling(2.0 * _radius),
                Wclass = WindowWindowClass.InputOnly,
                EventMask = (int) (Events
                    | EventMask.ButtonPressMask
                    | EventMask.ButtonReleaseMask
                    | EventMask.PointerMotionMask
                    | EventMask.TouchMask
                    | EventMask.EnterNotifyMask
                    | EventMask.LeaveNotifyMask),
            };

            const int attributesMask = (int) (WindowAttributesType.X | WindowAttributesType.Y);

            _eventWindow = new Gdk.Window(window, attributes, attributesMask);

            RegisterWindow(_eventWindow);
        }

        protected override void OnUnrealized()
        {
            if (_eventWindow != null)
            {
                UnregisterWindow(_eventWindow);
                _eventWindow.Destroy();
                _eventWindow = null;
            }

            OnSocketDestroyed();

            base.OnUnrealized();
        }

        protected override void OnSizeAllocated(Gdk.Rectangle allocation)
        {
            SetAllocation(allocation);

            if (!IsRealized)
                return;

            if (_eventWindow == null)
                return;

            _eventWindow.MoveResize(allocation.X, allocation.Y, (int) (2.0 * _radius), (int) (2.0 * _radius));
        }

        protected override bool OnDrawn(Context cr)
        {
            if (_io == NodeSocketIO.Disable)
                return false;

            cr.Save();
            cr.SetSourceRGBA(_rgba.Red, _rgba.Green, _rgba.Blue, _rgba.Alpha);
            cr.Arc(_radius, _radius, _radius, 0.0, 2.0 * Math.PI);
            cr.Fill();
            cr.Restore();

            return false;
        }

        protected override void OnGetPreferredWidth(out int minimum_width, out int natural_width)
        {
            minimum_width = (int) (2.0 * _radius);
            natural_width = minimum_width;
        }

        protected override void OnGetPreferredHeight(out int minimum_height, out int natural_height)
        {
            minimum_height = (int) (2.0 * _radius);
            natural_height = minimum_height;
        }

        protected override bool OnButtonPressEvent(EventButton _)
        {
            return true;
        }

        protected override bool OnMotionNotifyEvent(EventMotion _)
        {
            return true;
        }

        /// <summary>
        /// Emits a signal on the <see cref="NodeSocket"/> in incoming
        /// or outgoing direction.
        /// </summary>
        /// <param name="payload">The data object to write.</param>
        /// <returns>
        /// <c>true</c> on success, <c>false</c> if <see cref="NodeSocketIO.Disable"/> is configured.
        /// </returns>
        public bool Write(object payload)
        {
            if (_io == NodeSocketIO.Disable)
                return false;

            if (_io == NodeSocketIO.Sink)
                OnSocketDataIncoming(payload);

            if (_io == NodeSocketIO.Source)
                OnSocketDataOutgoing(payload);

            return true;
        }

        /// <summary>
        /// Drops all connections on a given socket. Attached sink or
        /// source sockets will be notified by the respective signals.
        /// </summary>
        public void Disconnect()
        {
            if (_input == null)
                return;

            _input.SocketDataOutgoingEvent -= DataIncomingSignalHandler;
            _input.SocketDisconnectEvent -= DisconnectSignalHandler;
            _input.SocketKeyChangeEvent -= KeyChangeSignalHandler;
            _input.SocketDestroyedEvent -= DestroyedSignalHandler;

            OnSocketDisconnected(_input);

            _input = null;
        }

        /// <summary>
        /// Explicitly establishes a connection between two sockets. If the sockets are
        /// not in the proper mode, the connection will fail. If the sink is already
        /// connected to a source, the source will be disconnected from the sink before
        /// connecting to the new source. If the compatibility keys of the sockets do not
        /// match, the connection will fail as well.
        /// </summary>
        /// <param name="sink">A <see cref="NodeSocket"/> in sink mode.</param>
        /// <param name="source">A <see cref="NodeSocket"/> in source mode.</param>
        public static void ConnectSockets(NodeSocket sink, NodeSocket source)
        {
            sink.ConnectSocketsInternal(source);
        }

        #endregion
    }
}
