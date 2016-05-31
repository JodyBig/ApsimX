﻿using System;
using System.Diagnostics;
using System.Windows.Forms;
using Glade;
using Gtk;
using WebKit;

namespace UserInterface.Views
{
    /// <summary>
    /// An interface for a HTML view.
    /// </summary>
    interface IHTMLView
    {
        /// <summary>
        /// Path to find images on.
        /// </summary>
        string ImagePath { get; set; }

        /// <summary>
        /// Set the contents of the control. Can be RTF, HTML or MarkDown. If 
        /// the contents are markdown and 'allowModification' = true then
        /// user will be able to edit markdown.
        /// </summary>
        void SetContents(string contents, bool allowModification);

        /// <summary>
        /// Return the edited markdown.
        /// </summary>
        /// <returns></returns>
        string GetMarkdown();

        /// <summary>
        /// Tells view to use a mono spaced font.
        /// </summary>
        void UseMonoSpacedFont();

    }

    interface IBrowserWidget
    {
        void Navigate(string uri);
        void LoadHTML(string html);
    }


    public class TWWebBrowserIE : IBrowserWidget
    {
        [System.Runtime.InteropServices.DllImportAttribute("user32.dll",
            EntryPoint = "SetParent")]
        internal static extern System.IntPtr
        SetParent([System.Runtime.InteropServices.InAttribute()] System.IntPtr
            hWndChild, [System.Runtime.InteropServices.InAttribute()] System.IntPtr
            hWndNewParent);

        public System.Windows.Forms.WebBrowser wb = null;
        public Gtk.Socket socket = null;
        public bool unmapped = false;

        public void InitIE(Gtk.Box w)
        {
            wb = new System.Windows.Forms.WebBrowser();
            w.SetSizeRequest(500, 500);
            wb.Height = 500; // w.GdkWindow.FrameExtents.Height;
            wb.Width = 500; // w.GdkWindow.FrameExtents.Width;
            wb.Navigate("about:blank");
            wb.Document.Write(String.Empty);

            socket = new Gtk.Socket();
            socket.SetSizeRequest(wb.Width, wb.Height);
            w.Add(socket);
            socket.Realize();
            socket.Show();
            socket.UnmapEvent += Socket_UnmapEvent;
            IntPtr browser_handle = wb.Handle;
            IntPtr window_handle = (IntPtr)socket.Id;
            SetParent(browser_handle, window_handle);
        }

        public void Remap()
        {
            // There are some quirks in the use of GTK sockets. I don't know why, but
            // once the socket has been "unmapped", we seem to lose it and its content.
            // In the GUI, this unmapping can occur either by switching to another tab, 
            // or by shrinking the window dimensions down to 0.
            // This explict remapping seems to correct the problem.
            if (socket != null)
            {
                socket.Unmap();
                socket.Map();
            }
            unmapped = false;
        }

        private void Socket_UnmapEvent(object o, UnmapEventArgs args)
        {
            unmapped = true;
        }

        public void Navigate(string uri)
        {
            wb.Navigate(uri);
        }

        public void LoadHTML(string html)
        {
            if (wb.Document.Body != null)
                // If we already have a document body, this is the more efficient
                // way to update its contents. It doesn't affect the scroll position
                // and doesn't make a little clicky sound.
                wb.Document.Body.InnerHtml = html;
            else
               wb.DocumentText = html;

            // Probably should make this conditional.
            // We use a timeout so we don't sit here forever if a document fails to load.

            Stopwatch watch = new Stopwatch();
            watch.Start();
            while (wb.ReadyState != WebBrowserReadyState.Complete && watch.ElapsedMilliseconds < 10000)
                while (Gtk.Application.EventsPending())
                    Gtk.Application.RunIteration();
        }

        public TWWebBrowserIE(Gtk.Box w)
        {
            InitIE(w);
        }
    }

    public class TWWebBrowserWK : IBrowserWidget
    {
        public WebView wb = null;
        public ScrolledWindow scrollWindow = new ScrolledWindow();

        public void InitWebKit(Gtk.Box w)
        {
            wb = new WebView();
            scrollWindow.Add(wb);
            // Hack to work around webkit bug; webkit will crash the app if a size is not provided
            // See https://bugs.eclipse.org/bugs/show_bug.cgi?id=466360 for a related bug report
            wb.SetSizeRequest(2, 2);
            w.PackStart(scrollWindow, true, true, 0);
            w.ShowAll();
        }

        public void Navigate(string uri)
        {
            wb.Open(uri);
        }

        public void LoadHTML(string html)
        {
            wb.LoadHtmlString(html, "about:blank");
        }

        public TWWebBrowserWK(Gtk.Box w)
        {
            InitWebKit(w);
        }
    }

    /// <summary>
    /// The Presenter for a HTML component.
    /// </summary>
    public class HTMLView : ViewBase, IHTMLView
    {
        /// <summary>
        /// Path to find images on.
        /// </summary>
        public string ImagePath { get; set; }

        [Widget]
        private VPaned vpaned1 = null;
        [Widget]
        private VBox vbox2 = null;
        [Widget]
        private Frame frame1 = null;
        [Widget]
        private HBox hbox1 = null;

        private IBrowserWidget browser = null;
        private MemoView memoView1;

        /// <summary>
        /// Constructor
        /// </summary>
        public HTMLView(ViewBase owner) : base(owner)
        {
            Glade.XML gxml = new Glade.XML("ApsimNG.Resources.Glade.HTMLView.glade", "vpaned1");
            gxml.Autoconnect(this);
            _mainWidget = vpaned1;
            // Handle a temporary browser created when we want to export a map.
            if (owner == null)
            {
                Gtk.Window win = new Gtk.Window(Gtk.WindowType.Popup);
                win.SetSizeRequest(500, 500);
                win.Add(MainWidget);
                win.ShowAll();
                while (Gtk.Application.EventsPending())
                    Gtk.Application.RunIteration();
                tempWindow = true;
            }
            memoView1 = new MemoView(this);
            hbox1.PackStart(memoView1.MainWidget, true, true, 0);
            vpaned1.PositionSet = true;
            vpaned1.Position = 200;
            hbox1.Visible = false;
            hbox1.NoShowAll = true;
            memoView1.ReadOnly = false;
            memoView1.MemoChange += this.TextUpdate;
            vpaned1.ShowAll();
            frame1.ExposeEvent += OnWidgetExpose;
            hbox1.Realized += Hbox1_Realized;
        }

        public override void Destroy()
        {
            if (memoView1 != null)
                memoView1.Destroy();
            if (tempWindow && _mainWidget != null && _mainWidget.IsRealized)
            {
                MainWidget.ParentWindow.Destroy();
            }
            base.Destroy();
        }

        private void Hbox1_Realized(object sender, EventArgs e)
        {
           vpaned1.Position = vpaned1.Parent.Allocation.Height / 2;
        }

        private void Frame1_Unrealized(object sender, EventArgs e)
        {
            if ((browser as TWWebBrowserIE) != null)
              (vbox2.Toplevel as Window).SetFocus -= MainWindow_SetFocus;
        }

        private void MainWindow_SetFocus(object o, SetFocusArgs args)
        {
            if (mainWindow != null)
                mainWindow.Focus(0);
        }

        /// <summary>
        /// Set the contents of the control. Can be RTF, HTML or MarkDown. If 
        /// the contents are markdown and 'allowModification' = true then
        /// user will be able to edit markdown.
        /// </summary>
        public void SetContents(string contents, bool allowModification)
        {
            TurnEditorOn(allowModification);
            if (contents != null)
            {
                if (allowModification)
                  memoView1.MemoText = contents;
                else
                  PopulateView(contents);
            }
        }

        protected bool tempWindow = false;
        /// <summary>
        /// Populate the view given the specified text.
        /// </summary>
        /// <param name="contents"></param>
        /// <param name="editingEnabled"></param>
        /// <returns></returns>
        private void PopulateView(string contents)
        {
            if (browser == null)
            {
                if (Environment.OSVersion.Platform.ToString().StartsWith("Win"))
                {
                    browser = new TWWebBrowserIE(vbox2);

                    /// UGH! Once the browser control gets keyboard focus, it doesn't relinquish it to 
                    /// other controls. It's actually a COM object, I guess, and running
                    /// with a different message loop, and probably in a different thread. 
                    /// 
                    /// Well, this hack works, more or less.
                    if (vbox2.Toplevel is Window)
                       (vbox2.Toplevel as Window).SetFocus += MainWindow_SetFocus;
                    frame1.Unrealized += Frame1_Unrealized;
                }
                else
                    browser = new TWWebBrowserWK(vbox2);
            }
            browser.LoadHTML(contents);
            //browser.Navigate("http://blend-bp.nexus.csiro.au/wiki/index.php");
        }

        // Although this isn't the obvious way to handle window resizing,
        // I couldn't find any better technique. 
        public void OnWidgetExpose(object o, ExposeEventArgs args)
        {
            int height, width;
            frame1.GdkWindow.GetSize(out width, out height);
            frame1.SetSizeRequest(width, height);
            if (browser is TWWebBrowserIE)
            {
                TWWebBrowserIE brow = browser as TWWebBrowserIE;
                if (brow.unmapped)
                {
                    brow.Remap();
                }

                if (brow.wb.Height != height || brow.wb.Width != width)
                {
                    brow.socket.SetSizeRequest(width, height);
                    brow.wb.Height = height;
                    brow.wb.Width = width;
                }
            }
        }

        /// <summary>
        /// Return the edited markdown.
        /// </summary>
        /// <returns></returns>
        public string GetMarkdown()
        {
            return memoView1.MemoText;
        }

        /// <summary>
        /// Tells view to use a mono spaced font.
        /// </summary>
        public void UseMonoSpacedFont() 
        {
        }

        /// <summary>
        /// Turn the editor on or off.
        /// </summary>
        /// <param name="turnOn"></param>
        private void TurnEditorOn(bool turnOn)
        {
            hbox1.Visible = turnOn;
        }

        /// <summary>
        /// Toggle edit / preview mode.
        /// </summary>
        private void ToggleEditMode()
        {
            bool editorIsOn = hbox1.Visible;  
            TurnEditorOn(!editorIsOn);   // toggle preview / edit mode.
        }

        #region Event Handlers

        /// <summary>
        /// User has clicked a link.
        /// </summary>
        /// <param name="sender">Sender of event.</param>
        /// <param name="e">Event arguments</param>
        private void OnLinkClicked(object sender, /* TBI LinkClicked */ EventArgs e)
        {
            /// TBI Process.Start(e.LinkText);
        }

        /// <summary>
        /// User has clicked 'edit'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnEditClick(object sender, EventArgs e)
        {
            TurnEditorOn(true);
        }

        private void TextUpdate(object sender, EventArgs e)
        {
            MarkdownDeep.Markdown markDown = new MarkdownDeep.Markdown();
            markDown.ExtraMode = true;
            string html = markDown.Transform(memoView1.MemoText);
            PopulateView(html);
        }

        #endregion

        private void OnHelpClick(object sender, EventArgs e)
        {
            Process.Start("https://www.apsim.info/Documentation/APSIM(nextgeneration)/Memo.aspx");
        }

        public void EnableWb(bool state)
        {
            if (browser is TWWebBrowserIE)
                (browser as TWWebBrowserIE).wb.Parent.Enabled = state;
        }
    }
}
