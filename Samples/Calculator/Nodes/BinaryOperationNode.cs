using System.Xml;
using Gtk;
using Gtk.NodeGraph;

using Node = Gtk.NodeGraph.Node;
using UI = Gtk.Builder.ObjectAttribute;

namespace Calculator.Nodes
{
    public class BinaryOperationNode : Node
    {
        private NodeSocket _a;
        private NodeSocket _b;
        private NodeSocket _o;

        [UI] private SpinButton _s1;
        [UI] private SpinButton _s2;
        [UI] private ComboBoxText _op;

        public BinaryOperationNode()
        {
            Label = "Operation";
            Spacing = 8;

            Width = 200;
            Height = 200;

            NodePadding = new Border
            {
                Top = 16,
                Bottom = 16,
                Left = 16,
                Right = 16,
            };

            _s1 = new SpinButton(0, double.MaxValue, 0.1);
            _s2 = new SpinButton(0, double.MaxValue, 0.1);

            _op = new ComboBoxText();
            _op.Append("+", "Addition");
            _op.Append("-", "Subtraction");
            _op.Append("*", "Multiplication");
            _op.Append("/", "Division");
            _op.Append("%", "Modulo");
            _op.Append("^", "Power");
        }

        private void OnSourceSocketDataIncoming(object sender, SocketDataEventArgs e)
        {
            if (!(sender is NodeSocket sink) || sink.IO != NodeSocketIO.Sink || sink.Input == null)
                return;

            if (sink == _a)
                _s1.Value = (double) e.Data;

            if (sink == _b)
                _s2.Value = (double) e.Data;
        }

        private void OnOperandValueChanged(object sender, System.EventArgs e)
        {
            _o.Write(DoOperation(_s1.Value, _s2.Value));
        }

        protected override void OnInitChildren()
        {
            HBox aBox = new HBox(false, 4);
            HBox bBox = new HBox(false, 4);

            _s1.Hexpand = true;
            _s2.Hexpand = true;

            aBox.Add(new Label("A") { Visible = true, Halign = Align.Start, Valign = Align.Center, Hexpand = false, Vexpand = false });
            aBox.Add(_s1);

            bBox.Add(new Label("B") { Visible = true, Halign = Align.Start, Valign = Align.Center, Hexpand = false, Vexpand = false });
            bBox.Add(_s2);

            _a = ItemAdd(aBox, NodeSocketIO.Sink, 1);

            Add(_op);

            _b = ItemAdd(bBox, NodeSocketIO.Sink, 1);

            _o = ItemAdd(new Label("Result") { Xalign = 1.0f }, NodeSocketIO.Source, 1);

            _s1.ValueChanged += OnOperandValueChanged;
            _s2.ValueChanged += OnOperandValueChanged;

            _op.Changed += OnOperandValueChanged;
        }

        protected override void OnNodeSocketConnect(NodeSocket sink, NodeSocket source)
        {
            base.OnNodeSocketConnect(sink, source);

            if (sink == _a)
                _s1.Visible = false;

            if (sink == _b)
                _s2.Visible = false;

            sink.SocketDataIncomingEvent += OnSourceSocketDataIncoming;

            _o.Write(DoOperation(_s1.Value, _s2.Value));
        }

        protected override void OnNodeSocketDisconnect(NodeSocket sink, NodeSocket source)
        {
            base.OnNodeSocketDisconnect(sink, source);

            if (sink == _a)
                _s1.Visible = true;

            if (sink == _b)
                _s2.Visible = true;

            sink.SocketDataIncomingEvent -= OnSourceSocketDataIncoming;

            _o.Write(DoOperation(_s1.Value, _s2.Value));
        }

        public override XmlNode[] ExportProperties()
        {
            XmlDocument xmlDoc = new XmlDocument();

            static XmlElement CreateChildElement(string name, XmlDocument xmlDoc)
            {
                XmlNode node = xmlDoc.CreateNode(XmlNodeType.Element, "child", string.Empty);
                XmlAttribute attr = (XmlAttribute) xmlDoc.CreateNode(XmlNodeType.Attribute, "internal-child", string.Empty);
                attr.Value = name;
                node.Attributes.Append(attr);

                return (XmlElement) node;
            }

            static XmlElement CreateObjectElement(GLib.Object o, string name, XmlDocument xmlDoc)
            {
                XmlNode node = xmlDoc.CreateNode(XmlNodeType.Element, "object", string.Empty);
                XmlAttribute attr = (XmlAttribute) xmlDoc.CreateNode(XmlNodeType.Attribute, "class", string.Empty);
                attr.Value = o.NativeType.ToString();
                node.Attributes.Append(attr);
                attr = (XmlAttribute) xmlDoc.CreateNode(XmlNodeType.Attribute, "id", string.Empty);
                attr.Value = name;
                node.Attributes.Append(attr);

                return (XmlElement) node;
            }

            static XmlElement CreatePropertyElement(string property, string value, XmlDocument xmlDoc)
            {
                XmlNode node = xmlDoc.CreateNode(XmlNodeType.Element, "property", string.Empty);
                XmlAttribute attr = (XmlAttribute) xmlDoc.CreateNode(XmlNodeType.Attribute, "name", string.Empty);
                attr.Value = property;
                node.Attributes.Append(attr);
                node.AppendChild(xmlDoc.CreateTextNode(value.ToString()));

                return (XmlElement) node;
            }

            XmlNode[] nodes = new XmlNode[2];

            XmlElement node = CreateChildElement(nameof(_s1), xmlDoc);
            XmlElement nodeObject = CreateObjectElement(_s1, nameof(_s1), xmlDoc);
            nodeObject.AppendChild(CreatePropertyElement("digits", _s1.Digits.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("value", _s1.Value.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("adjustment", "s1Adjustment", xmlDoc));
            node.AppendChild(nodeObject);
            nodeObject = CreateObjectElement(_s1.Adjustment, "s1Adjustment", xmlDoc);
            nodeObject.AppendChild(CreatePropertyElement("lower", _s1.Adjustment.Lower.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("upper", _s1.Adjustment.Upper.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("value", _s1.Adjustment.Value.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("step_increment", _s1.Adjustment.StepIncrement.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("page_increment", _s1.Adjustment.PageIncrement.ToString(), xmlDoc));
            node.AppendChild(nodeObject);
            nodes[0] = node;

            node = CreateChildElement(nameof(_s2), xmlDoc);
            nodeObject = CreateObjectElement(_s2, nameof(_s2), xmlDoc);
            nodeObject.AppendChild(CreatePropertyElement("digits", _s2.Digits.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("value", _s2.Value.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("adjustment", "s2Adjustment", xmlDoc));
            node.AppendChild(nodeObject);
            nodeObject = CreateObjectElement(_s2.Adjustment, "s2Adjustment", xmlDoc);
            nodeObject.AppendChild(CreatePropertyElement("lower", _s2.Adjustment.Lower.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("upper", _s2.Adjustment.Upper.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("value", _s2.Adjustment.Value.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("step_increment", _s2.Adjustment.StepIncrement.ToString(), xmlDoc));
            nodeObject.AppendChild(CreatePropertyElement("page_increment", _s2.Adjustment.PageIncrement.ToString(), xmlDoc));
            node.AppendChild(nodeObject);
            nodes[1] = node;

            return nodes;
        }

        public double DoOperation(double a, double b)
        {
            return _op.ActiveId switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => a / b,
                "%" => a % b,
                "^" => System.Math.Pow(a, b),
                _ => 0,
            };
        }
    }
}
