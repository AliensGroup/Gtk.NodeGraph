using System;
using Calculator.Nodes;
using Gtk;
using Gtk.NodeGraph;

using NodeView = Gtk.NodeGraph.NodeView;
using UI = Gtk.Builder.ObjectAttribute;

namespace Calculator
{
    public class MainWindow : Window
    {
        [UI] private Alignment _alignment;
        [UI] private Button _addOperationButton;
        [UI] private Button _addResultButton;

        [UI] private Button _saveButton;
        [UI] private Button _loadButton;

        private NodeView _nodeView;

        public MainWindow() : this(new Builder("MainWindow.glade")) { }

        private MainWindow(Builder builder) : base(builder.GetObject("MainWindow").Handle)
        {
            builder.Autoconnect(this);

            DeleteEvent += Window_DeleteEvent;

            _addOperationButton.Clicked += AddOperationButton_Clicked;
            _addResultButton.Clicked += AddResultButton_Clicked;

            _saveButton.Clicked += SaveButton_Clicked;
            _loadButton.Clicked += LoadButton_Clicked;

            _nodeView = new NodeView();

            _alignment.Child = _nodeView;
        }

        private void Window_DeleteEvent(object sender, DeleteEventArgs a)
        {
            Application.Quit();
        }

        private void AddOperationButton_Clicked(object sender, EventArgs a)
        {
            _nodeView.Add(new BinaryOperationNode());
        }

        private void AddResultButton_Clicked(object sender, EventArgs a)
        {
            _nodeView.Add(new ResultNode());
        }

        private void SaveButton_Clicked(object sender, EventArgs a)
        {
        }

        private void LoadButton_Clicked(object sender, EventArgs a)
        {
        }
    }
}
