using System;
using System.Reflection;
namespace Nekres.Music_Mixer.Core {
    public static class ObjectExtensions {
        /// <summary>
        /// Tries to get the <see cref="FieldInfo"/> of a private member matching <see cref="fieldName"/> from<br/>
        /// either the type of the <see cref="target"/> object itself or its most-derived base type (unlike '<see cref="Type.GetField(string)"/>').
        /// </summary>
        /// <exception cref="ArgumentNullException">If <see cref="target"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">If <see cref="fieldName"/> is <see langword="null"/> or <see cref="string.Empty"/>.</exception>
        /// <returns><see cref="FieldInfo"/> or <see langword="null"/> if no match is found.</returns>
        public static FieldInfo GetPrivateField(this object target, string fieldName) {
            if (target == null) {
                throw new ArgumentNullException(nameof(target), "The assignment target cannot be null.");
            }

            if (string.IsNullOrEmpty(fieldName)) {
                throw new ArgumentException("The field name cannot be null or empty.", nameof(fieldName));
            }

            var t = target.GetType();

            const BindingFlags BF = BindingFlags.Instance  |
                                    BindingFlags.NonPublic |
                                    BindingFlags.DeclaredOnly;

            FieldInfo fi;

            while ((fi = t.GetField(fieldName, BF)) == null && (t = t.BaseType) != null) {
                /* NOOP */
            }

            return fi;
        }
    }
}
