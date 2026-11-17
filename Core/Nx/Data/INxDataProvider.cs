using System;

namespace nyxWM.Nx
{
    /// <summary>
    /// One value source exposed to .nx bindings. Providers are registered under a
    /// namespace such as "time" or "music", so "{time.hh}" resolves through the
    /// "time" provider and "{music.title}" through "music".
    ///
    /// Providers must be cheap to poll: the runtime samples them at a rate the
    /// document asks for, not once per frame, so a provider may read the registry
    /// or enumerate sessions as long as it caches the expensive part.
    /// </summary>
    internal interface INxDataProvider
    {
        /// <summary>Namespace used in bindings, e.g. "time".</summary>
        string Namespace { get; }

        /// <summary>
        /// Current value of <paramref name="key"/>, optionally narrowed by
        /// <paramref name="argument"/>. Returns null when the key is unknown, so
        /// the runtime can render an empty string instead of failing.
        /// </summary>
        NxValue? Read(string key, string? argument);

        /// <summary>Keys this provider can answer, for the documentation and editor hints.</summary>
        System.Collections.Generic.IEnumerable<string> Keys { get; }
    }

    /// <summary>Base class with the null-tolerant plumbing every provider repeats.</summary>
    internal abstract class NxProviderBase : INxDataProvider
    {
        public abstract string Namespace { get; }

        public abstract System.Collections.Generic.IEnumerable<string> Keys { get; }

        public abstract NxValue? Read(string key, string? argument);

        protected static NxValue Text(string value) => NxValue.FromText(value, 0);

        protected static NxValue Number(double value) => NxValue.FromNumber(value, 0);

        protected static NxValue Flag(bool value) => NxValue.FromBoolean(value, 0);

        protected static NxValue Color(NxColor value) => NxValue.FromColor(value, 0);

        protected static NxValue Empty => NxValue.FromText(string.Empty, 0);
    }
}
