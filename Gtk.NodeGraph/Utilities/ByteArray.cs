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

using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Gtk.NodeGraph.Utilities
{
    /// <summary>
    /// Utility class used to manipulate array of bytes.
    /// </summary>
    public static class ByteArray
    {
        /// <summary>
        /// Converts the given object to an array of bytes using
        /// the standard <see cref="BinaryFormatter" />.
        /// </summary>
        /// <param name="obj">The object to convert in array of bytes.</param>
        /// <typeparam name="T">The object's type.</typeparam>
        /// <returns>
        /// An array of bytes representing the given object.
        /// </returns>
        public static byte[] ObjectToByteArray<T>(T obj)
            where T : class
        {
            if (obj == null)
                return null;

            BinaryFormatter bf = new BinaryFormatter();
            using (MemoryStream ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.GetBuffer();
            }
        }

        /// <summary>
        /// Converts an array of bytes to an object's instance of type <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// This methods is intended to be used with bytes arrays created using <see cref="ObjectToByteArray{T}"/>.
        /// </remarks>
        /// <param name="bytes">The array of bytes to convert.</param>
        /// <typeparam name="T">The type of the resulting object.</typeparam>
        /// <returns>
        /// An instance of type <typeparamref name="T"/>, or null in case of failing.
        /// </returns>
        public static T ByteArrayToObject<T>(byte[] bytes)
            where T : class
        {
            using (MemoryStream memStream = new MemoryStream())
            {
                BinaryFormatter binForm = new BinaryFormatter();
                memStream.Write(bytes, 0, bytes.Length);
                memStream.Seek(0, SeekOrigin.Begin);

                return binForm.Deserialize(memStream) as T;
            }
        }
    }
}
