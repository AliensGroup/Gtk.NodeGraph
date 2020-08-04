using Gtk;
using Gtk.NodeGraph;

using Node = Gtk.NodeGraph.Node;

namespace Calculator.Nodes
{
    public class ResultNode : Node
    {
        private NodeSocket _nodeSocket;
        private Label _value;

        public ResultNode()
        {
            Label = "Result";


            _value = new Label("0") { Xalign = -1.0f };
            _nodeSocket = ItemAdd(_value, NodeSocketIO.Sink, 1);
        }

        private void OnSourceSocketDataOutgoingEvent(object sender, SocketDataEventArgs e)
        {
            _value.Text = e.Data.ToString();
        }

        protected override void OnNodeSocketConnect(NodeSocket sink, NodeSocket source)
        {
            base.OnNodeSocketConnect(sink, source);

            source.SocketDataOutgoingEvent += OnSourceSocketDataOutgoingEvent;
        }

        protected override void OnNodeSocketDisconnect(NodeSocket sink, NodeSocket source)
        {
            base.OnNodeSocketDisconnect(sink, source);

            _value.Text = "0";
            source.SocketDataOutgoingEvent -= OnSourceSocketDataOutgoingEvent;
        }
    }
}
