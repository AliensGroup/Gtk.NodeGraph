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

namespace Gtk.NodeGraph
{
    /// <summary>
    /// Defines the <see cref="NodeSocket"/> behavior.
    /// </summary>
    public enum NodeSocketIO
    {
        /// <summary>
        /// The node socket is disabled.
        /// </summary>
        Disable,

        /// <summary>
        /// The node socket is in input mode.
        /// It can only receive data from only one
        /// other node socket at time.
        /// </summary>
        Sink,

        /// <summary>
        /// The node socket is in output mode.
        /// It can only send data to one or many other
        /// node sockets at time.
        /// </summary>
        Source,
    }
}
