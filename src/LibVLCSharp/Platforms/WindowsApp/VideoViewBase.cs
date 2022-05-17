using System;
using System.Runtime.InteropServices;
using DirectN;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using WinRT;

namespace LibVLCSharp.Platforms.WindowsApp
{
    /// <summary>
    /// VideoView base class for the UWP platform
    /// </summary>
    [TemplatePart(Name = PartSwapChainPanelName, Type = typeof(SwapChainPanel))]
    public abstract class VideoViewBase : Control, IVideoView
    {
        private const string PartSwapChainPanelName = "SwapChainPanel";

        SwapChainPanel? _panel;
        IComObject<ID3D11DeviceContext>? _deviceContext;
        IComObject<ID3D11Device>? _d3d11Device;
        IComObject<IDXGIDevice1>? _device1;
        IComObject<IDXGIDevice3>? _device3;
        IComObject<IDXGISwapChain2>? _swapChain2;
        IComObject<IDXGISwapChain1>? _swapChain1;
        bool _loaded;

        /// <summary>
        /// The constructor
        /// </summary>
        public VideoViewBase()
        {
            DefaultStyleKey = typeof(VideoViewBase);

            if (!DesignMode.DesignModeEnabled)
            {
                Unloaded += (s, e) => DestroySwapChain();
            }
        }

        /// <summary>
        /// Invoked whenever application code or internal processes (such as a rebuilding layout pass) call ApplyTemplate. 
        /// In simplest terms, this means the method is called just before a UI element displays in your app.
        /// Override this method to influence the default post-template logic of a class.
        /// </summary>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _panel = (SwapChainPanel)GetTemplateChild(PartSwapChainPanelName);

            if (DesignMode.DesignModeEnabled)
                return;

            DestroySwapChain();

            _panel.SizeChanged += (s, eventArgs) =>
            {
                if (_loaded)
                {
                    if (eventArgs.PreviousSize != eventArgs.NewSize)
                    {
                        UpdateSize();
                    }
                }
                else
                {
                    CreateSwapChain();
                }
            };

            _panel.CompositionScaleChanged += (s, eventArgs) =>
            {
                if (_loaded)
                {
                    UpdateScale();
                }
            };

        }

        /// <summary>
        /// Gets the swapchain parameters to pass to the <see cref="LibVLC"/> constructor.
        /// If you don't pass them to the <see cref="LibVLC"/> constructor, the video won't
        /// be displayed in your application.
        /// Calling this property will throw an <see cref="InvalidOperationException"/> if the VideoView is not yet full Loaded.
        /// </summary>
        /// <returns>The list of arguments to be given to the <see cref="LibVLC"/> constructor.</returns>
        public string[] SwapChainOptions
        {
            get
            {
                if (!_loaded)
                {
                    throw new InvalidOperationException("You must wait for the VideoView to be loaded before calling GetSwapChainOptions()");
                }
                var deviceContextPtr = Marshal.GetComInterfaceForObject(_deviceContext!.Object, typeof(ID3D11DeviceContext));
                var swapChainPtr = Marshal.GetComInterfaceForObject(_swapChain1!.Object, typeof(IDXGISwapChain1));
                return new string[]
                {
                    $"--winrt-d3dcontext=0x{deviceContextPtr.ToString("x")}",
                    $"--winrt-swapchain=0x{swapChainPtr.ToString("x")}"
                };
            }
        }

        /// <summary>
        /// Called when the video view is fully loaded
        /// </summary>
        protected abstract void OnInitialized();

        /// <summary>
        /// Initializes the SwapChain for use with LibVLC
        /// </summary>
        void CreateSwapChain()
        {
            // Do not create the swapchain when the VideoView is collapsed.
            if (_panel == null || _panel.ActualHeight == 0)
                return;


            IComObject<IDXGIFactory2>? dxgiFactory = null;
            try
            {
                var deviceCreationFlags =
                    D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT;

#if DEBUG
                try
                {
                    dxgiFactory = DXGIFunctions.CreateDXGIFactory2(DXGI_CREATE_FACTORY_FLAGS.DXGI_CREATE_FACTORY_DEBUG);
                }
                catch (Exception)
                {
                    dxgiFactory = DXGIFunctions.CreateDXGIFactory2();
                }
#else
                dxgiFactory = DXGIFunctions.CreateDXGIFactory2();
#endif

                _d3d11Device = D3D11Functions.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, deviceCreationFlags, out _deviceContext);

                if (_deviceContext is null)
                {
                    throw new VLCException("Could not create Direct3D11 device : No compatible adapter found.");
                }

                var desc = new DXGI_SWAP_CHAIN_DESC1();
                desc.Width = (uint)(_panel.ActualWidth * _panel.CompositionScaleX);
                desc.Height = (uint)(_panel.ActualHeight * _panel.CompositionScaleY);
                desc.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;
                desc.Stereo = false;
                desc.SampleDesc.Count = 1;
                desc.SampleDesc.Quality = 0;
                desc.BufferUsage = DirectN.Constants.DXGI_USAGE_RENDER_TARGET_OUTPUT;
                desc.BufferCount = 2;
                desc.SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;  //improtant for XAML SwapChainPanel
                desc.Flags = 0;
                desc.AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_UNSPECIFIED;
                desc.Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH; //improtant for XAML SwapChainPanel

                IDXGIDevice1 dxgiDevice = _d3d11Device.As<IDXGIDevice1>(true);
                _device1 = new ComObject<IDXGIDevice1>(dxgiDevice);
                _device3 = new ComObject<IDXGIDevice3>(_device1.As<IDXGIDevice3>(true));
                _swapChain1 = dxgiFactory.CreateSwapChainForComposition<IDXGISwapChain1>(_device1, desc);
                _swapChain2 = new ComObject<IDXGISwapChain2>(_swapChain1.As<IDXGISwapChain2>(true));

                var nativepanel = _panel.As<ISwapChainPanelNative>();
                nativepanel.SetSwapChain(_swapChain1!.Object);

                UpdateSize();
                UpdateScale();
                _loaded = true;
                OnInitialized();
            }
            catch (Exception ex)
            {
                DestroySwapChain();
                throw new VLCException("DX operation failed, see InnerException for details", ex);

                throw;
            }
            finally
            {
                dxgiFactory?.Dispose();
            }
        }

        /// <summary>
        /// Destroys the SwapChain and all related instances.
        /// </summary>
        void DestroySwapChain()
        {
            _swapChain2?.Dispose();
            _swapChain2 = null;

            _device3?.Dispose();
            _device3 = null;

            _swapChain1?.Dispose();
            _swapChain1 = null;

            _device1?.Dispose();
            _device1 = null;

            _deviceContext?.Dispose();
            _deviceContext = null;

            _loaded = false;
        }

        object _CriticalLock = new object();

        /// <summary>
        /// Associates width/height private data into the SwapChain, so that VLC knows at which size to render its video.
        /// </summary>
        void UpdateSize()
        {
            if (_panel is null || _swapChain1 is null || _swapChain1.IsDisposed)
                return;

            lock (_CriticalLock)
            {
                _swapChain1!.Object.ResizeBuffers(2, (uint)_panel.ActualWidth, (uint)_panel.ActualHeight, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, 0);
            }
        }

        /// <summary>
        /// Updates the MatrixTransform of the SwapChain.
        /// </summary>
        void UpdateScale()
        {
            if (_panel is null) return;
            _swapChain2!.Object.SetMatrixTransform(new DXGI_MATRIX_3X2_F { _11 = 1.0f / _panel.CompositionScaleX, _22 = 1.0f / _panel.CompositionScaleY });
        }

        /// <summary>
        /// When the app is suspended, UWP apps should call Trim so that the DirectX data is cleaned.
        /// </summary>
        public void Trim()
        {
            _device3?.Object.Trim();
        }

        /// <summary>
        /// When the media player is attached to the view.
        /// </summary>
        void Attach()
        {
        }

        /// <summary>
        /// When the media player is detached from the view.
        /// </summary>
        void Detach()
        {
        }


        /// <summary>
        /// Identifies the <see cref="MediaPlayer"/> dependency property.
        /// </summary>
        public static DependencyProperty MediaPlayerProperty { get; } = DependencyProperty.Register(nameof(MediaPlayer), typeof(MediaPlayer),
            typeof(VideoViewBase), new PropertyMetadata(null, OnMediaPlayerChanged));
        /// <summary>
        /// MediaPlayer object connected to the view
        /// </summary>
        public MediaPlayer? MediaPlayer
        {
            get => (MediaPlayer?)GetValue(MediaPlayerProperty);
            set => SetValue(MediaPlayerProperty, value);
        }

        private static void OnMediaPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var videoView = (VideoViewBase)d;
            videoView.Detach();
            if (e.NewValue != null)
            {
                videoView.Attach();
            }
        }
    }
}
