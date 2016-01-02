using System;
using Gtk;
using System.Net;
using System.IO;

namespace MeetupScraper
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			Application.Init ();
			MainWindow win = new MainWindow ();
			win.Show ();
			Application.Run ();
		}
	}
}
