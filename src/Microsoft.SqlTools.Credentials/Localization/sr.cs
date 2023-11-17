// WARNING:
// This file was generated by the Microsoft SQL Server String Resource Tool 8.0.0.0
// from information in sr.strings
// DO NOT MODIFY THIS FILE'S CONTENTS, THEY WILL BE OVERWRITTEN
//
// <auto-generated />

namespace Microsoft.SqlTools.Credentials
{
    using System;
    using System.Reflection;
    using System.Resources;
    using System.Globalization;

    [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class SR
    {
        protected SR()
        { }

        public static CultureInfo Culture
        {
            get
            {
                return Keys.Culture;
            }
            set
            {
                Keys.Culture = value;
            }
        }


        public static string CredentialsServiceInvalidCriticalHandle
        {
            get
            {
                return Keys.GetString(Keys.CredentialsServiceInvalidCriticalHandle);
            }
        }

        public static string CredentialsServicePasswordLengthExceeded
        {
            get
            {
                return Keys.GetString(Keys.CredentialsServicePasswordLengthExceeded);
            }
        }

        public static string CredentialsServiceTargetForDelete
        {
            get
            {
                return Keys.GetString(Keys.CredentialsServiceTargetForDelete);
            }
        }

        public static string CredentialsServiceTargetForLookup
        {
            get
            {
                return Keys.GetString(Keys.CredentialsServiceTargetForLookup);
            }
        }

        public static string CredentialServiceWin32CredentialDisposed
        {
            get
            {
                return Keys.GetString(Keys.CredentialServiceWin32CredentialDisposed);
            }
        }

        [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
        public class Keys
        {
            static ResourceManager resourceManager = new ResourceManager("Microsoft.SqlTools.Credentials.Localization.SR", typeof(SR).GetTypeInfo().Assembly);

            static CultureInfo _culture = null;


            public const string CredentialsServiceInvalidCriticalHandle = "CredentialsServiceInvalidCriticalHandle";


            public const string CredentialsServicePasswordLengthExceeded = "CredentialsServicePasswordLengthExceeded";


            public const string CredentialsServiceTargetForDelete = "CredentialsServiceTargetForDelete";


            public const string CredentialsServiceTargetForLookup = "CredentialsServiceTargetForLookup";


            public const string CredentialServiceWin32CredentialDisposed = "CredentialServiceWin32CredentialDisposed";


            private Keys()
            { }

            public static CultureInfo Culture
            {
                get
                {
                    return _culture;
                }
                set
                {
                    _culture = value;
                }
            }

            public static string GetString(string key)
            {
                return resourceManager.GetString(key, _culture);
            }

        }
    }
}
