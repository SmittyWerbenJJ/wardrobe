using System.Collections.Generic;
using UnityEngine;

namespace SmittyWerben{

    public class VdTextureLoader
    {
        public const int TypeDiffuse = 0;
        public const int TypeNormal  = 1;
        public const int TypeSpecular = 2;
        public const int TypeGloss    = 3;

        public delegate void TextureCallback( Texture2D t2d );

        private Dictionary< string, TextureState > _textureCache = new Dictionary< string, TextureState >();

        /**
         * Load (or reuse) a texture from a file to perform an action.
         */
        public void WithTexture( string textureFile, int textureType, TextureCallback action )
        {
            if( _textureCache.ContainsKey( textureFile ) )
            {
                _textureCache[ textureFile ].WithTexture( action );
            }
            else
            {
                // Create the texture state object
                TextureState newState = new TextureState();
                newState.WithTexture( action );
                _textureCache[ textureFile ] = newState;

                bool createMipMaps = true;
                bool linear = false;
                bool isNormal = false;
                bool compress = true;

                switch( textureType )
                {
                    case TypeSpecular:
                    case TypeGloss:
                        linear = true;
                        break;

                    case TypeNormal:
                        linear = true;
                        isNormal = true;
                        compress = false;
                        break;

                    default:
                        break;
                }

                // Begin loading the texture
                var img = new ImageLoaderThreaded.QueuedImage
                {
                    imgPath = textureFile,
                    callback = qimg => newState.ApplyTexture( qimg ),
                    createMipMaps = createMipMaps,
                    isNormalMap = isNormal,
                    linear = linear,
                    compress = compress
                };

                ImageLoaderThreaded.singleton.QueueImage( img );
            }
        }

        /**
         * Expire (remove) textures from the cache so that they can be reloaded.
         */
        public void ExpireDirectory( string directory )
        {
            string dirLower = directory.ToLower();
            List< string > files = new List<string>();
            foreach( KeyValuePair< string, TextureState > file in _textureCache )
            {
                if( file.Key.ToLower().StartsWith( dirLower ) )
                {
                    files.Add( file.Key );
                }
            }

            files.ForEach( f => _textureCache.Remove( f ) );
        }

        // A simple class to maintain the state of, and act on, loaded textures
        private class TextureState
        {
            private Texture2D _loadedTexture;

            private TextureCallback _loadingCallback = Blank;

            /* Take an action if/when the texture is loaded.
             */
            public void WithTexture( TextureCallback callback )
            {
                if( _loadedTexture != null )
                {
                    callback.Invoke( _loadedTexture );
                }
                else
                {
                    TextureCallback prev = _loadingCallback;
                    _loadingCallback = (t2d) => { prev.Invoke( t2d ); callback.Invoke( t2d ); };
                }
            }

            public void ApplyTexture( ImageLoaderThreaded.QueuedImage tex )
            {
                if( tex.hadError )
                {
                    SuperController.LogError( "Error loading texture: " + tex.errorText );
                }
                else
                {
                    _loadedTexture = tex.tex;
                    _loadingCallback.Invoke( tex.tex );
                    _loadingCallback = Blank;
                }
            }

            private static void Blank( Texture2D t2d )
            {
                // This space intentionally blank
            }
        }
    }
}
