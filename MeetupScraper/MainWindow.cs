using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Gtk;
using Newtonsoft.Json;
using ICSharpCode.SharpZipLib.Zip;
using SystemPath = System.IO.Path;
using MeetupScraper;

public partial class MainWindow: Gtk.Window
{	
	private string getJsonFromUrl(string url)
	{
		ServicePointManager.ServerCertificateValidationCallback += new RemoteCertificateValidationCallback((sender, certificate, chain, policyErrors) => { return true; });
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create (url);
		req.Accept = "application/json;odata=verbose";
		HttpWebResponse resp = (HttpWebResponse)req.GetResponse ();
		StreamReader sr = new StreamReader (resp.GetResponseStream ());
		string json = sr.ReadToEnd ();
		sr.Close ();
		resp.Close ();
		return json;
	}
	
	bool continueDownloading = true;

	void DownloadMeetups()
	{
		using (StreamWriter logFile = File.AppendText("log.txt"))
		{
			logFile.AutoFlush = true;
			if (continueDownloading)
			{
				JsonMeetupGroup meetupGroup = JsonConvert.DeserializeObject<JsonMeetupGroup> (getJsonFromUrl ("https://api.meetup.com/YoungOutdoorAdventurersofOrlando?photo-host=public&sig_id=3080124&sig=a585449de92e7c85f06e242e595b375697aa3805"));
				JsonMeetupEvents meetupEvents = JsonConvert.DeserializeObject<JsonMeetupEvents> (getJsonFromUrl ("https://api.meetup.com/2/events?&sign=true&photo-host=public&group_urlname=" + meetupGroup.urlname + "&group_id=" + meetupGroup.id + "&status=past&page=20"));
				int totalMeetupEvents = meetupEvents.meta.total_count;
				int meetupeventsProcessed = 0;

				List<string> eventIds = new List<string> ();

				using (WebClient client = new WebClient())
				{
					for (int eventOffset = 0; eventOffset*200 < totalMeetupEvents; eventOffset++)
					{
						if (continueDownloading)
						{
							meetupEvents = JsonConvert.DeserializeObject<JsonMeetupEvents> (getJsonFromUrl ("https://api.meetup.com/2/events?&sign=true&photo-host=public&group_id=" + meetupGroup.id + "&status=past&page=200&offset=" + eventOffset));

							foreach (JsonMeetupEvents.MeetupEvent meetupEvent in meetupEvents.results)
							{
								meetupeventsProcessed++;
								progressbar1.Fraction = (float)meetupeventsProcessed / totalMeetupEvents;
								label1.Text = "Downloading photos from " + meetupEvent.name;

								bool createdDirectory = false;
								eventIds.Add (meetupEvent.id);
								int seconds = (int)(meetupEvent.time / 1000);

								DateTime epoch = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
								DateTime eventTime = epoch.AddSeconds (seconds);

								string meetupEventName = meetupEvent.name.Replace ('/', '_').Replace ('\\', '_');
								string eventTimeString = eventTime.ToString ("yyyy-MM-dd h:mm tt", CultureInfo.InvariantCulture);

								logFile.Write("Processing " + eventTimeString + " - " + meetupEventName + "...");

								string directory = "/home/mbuchoff/Pictures/" + eventTimeString + " - " + meetupEventName;

								if (continueDownloading)
								{
									JsonPhotoAlbums photoAlbums = JsonConvert.DeserializeObject<JsonPhotoAlbums> (getJsonFromUrl ("https://api.meetup.com/2/photo_albums?&sign=true&photo-host=public&group_id=" + meetupGroup.id + "&event_id=" + meetupEvent.id + "&page=20"));

									for (int photoAlbumIndex = 0; photoAlbumIndex < photoAlbums.meta.total_count; photoAlbumIndex++)
									{
										JsonPhotoAlbums.PhotoAlbum photoAlbum = photoAlbums.results [photoAlbumIndex];
										int totalPhotos = photoAlbum.photo_count;

										for (int photoOffset = 0; photoOffset*20 < totalPhotos && continueDownloading; photoOffset++)
										{
											JsonMeetupPhoto[] photos = JsonConvert.DeserializeObject<JsonMeetupPhoto[]> (getJsonFromUrl ("https://api.meetup.com/YoungOutdoorAdventurersofOrlando/photo_albums/" + photoAlbum.photo_album_id + "/photos?&sign=true&photo-host=public&page=20&offset=" + photoOffset));

											foreach (JsonMeetupPhoto photo in photos)
											{
												if (!createdDirectory)
												{
													Directory.CreateDirectory (directory);
													createdDirectory = true;
												}

												if (continueDownloading)
												{
													client.DownloadFile (photo.highres_link, directory + "/" + photo.id + ".jpeg");
												}
											}
										}
									}

									if (createdDirectory)
									{
										CompressDirectory (directory, directory + ".zip");
									}
								}

								logFile.WriteLine ();
							}
						}
					}
				}
			}
		}
	}

	void CompressDirectory(string directoryInput, string zipOutput)
	{
		// Depending on the directory this could be very large and would require more attention
		// in a commercial package.
		string[] filenames = Directory.GetFiles(directoryInput);

		// 'using' statements guarantee the stream is closed properly which is a big source
		// of problems otherwise.  Its exception safe as well which is great.
		using (ZipOutputStream s = new ZipOutputStream(File.Create(zipOutput))) {

			s.SetLevel(9); // 0 - store only to 9 - means best compression

			byte[] buffer = new byte[4096];

			foreach (string file in filenames) {

				// Using GetFileName makes the result compatible with XP
				// as the resulting path is not absolute.
				ZipEntry entry = new ZipEntry(SystemPath.GetFileName(file));

				// Setup the entry data as required.

				// Crc and size are handled by the library for seakable streams
				// so no need to do them here.

				// Could also use the last write time or similar for the file.
				entry.DateTime = DateTime.Now;
				s.PutNextEntry(entry);

				using ( FileStream fs = File.OpenRead(file) ) {

					// Using a fixed size buffer here makes no noticeable difference for output
					// but keeps a lid on memory usage.
					int sourceBytes;
					do {
						sourceBytes = fs.Read(buffer, 0, buffer.Length);
						s.Write(buffer, 0, sourceBytes);
					} while ( sourceBytes > 0 );
				}
			}

			// Finish/Close arent needed strictly as the using statement does this automatically

			// Finish is important to ensure trailing information for a Zip file is appended.  Without this
			// the created file would be invalid.
			s.Finish();

			// Close is important to wrap things up and unlock the file.
			s.Close();
		}
	}

	void delete_event (object obj, DeleteEventArgs args)
	{
		continueDownloading = false;
	}

	public MainWindow (): base (Gtk.WindowType.Toplevel)
	{
		Build ();
		DeleteEvent += delete_event;

		Thread workerThread = new Thread(DownloadMeetups);
		workerThread.Start ();
	}

	private static bool RemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
	{
		//Return true if the server certificate is ok
		if (sslPolicyErrors == SslPolicyErrors.None)
			return true;

		bool acceptCertificate = true;
		string msg = "The server could not be validated for the following reason(s):\r\n";

		//The server did not present a certificate
		if ((sslPolicyErrors &
		     SslPolicyErrors.RemoteCertificateNotAvailable) == SslPolicyErrors.RemoteCertificateNotAvailable)
		{
			msg = msg + "\r\n    -The server did not present a certificate.\r\n";
			acceptCertificate = false;
		}
		else
		{
			//The certificate does not match the server name
			if ((sslPolicyErrors &
			     SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.RemoteCertificateNameMismatch)
			{
				msg = msg + "\r\n    -The certificate name does not match the authenticated name.\r\n";
				acceptCertificate = false;
			}

			//There is some other problem with the certificate
			if ((sslPolicyErrors &
			     SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.RemoteCertificateChainErrors)
			{
				foreach (X509ChainStatus item in chain.ChainStatus)
				{
					if (item.Status != X509ChainStatusFlags.RevocationStatusUnknown &&
					    item.Status != X509ChainStatusFlags.OfflineRevocation)
						break;

					if (item.Status != X509ChainStatusFlags.NoError)
					{
						msg = msg + "\r\n    -" + item.StatusInformation;
						acceptCertificate = false;
					}
				}
			}
		}

		//If Validation failed, present message box
		if (acceptCertificate == false)
		{
			msg = msg + "\r\nDo you wish to override the security check?";
			//          if (MessageBox.Show(msg, "Security Alert: Server could not be validated",
			//                       MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
			acceptCertificate = true;
		}

		return acceptCertificate;
	}

	protected void OnDeleteEvent (object sender, DeleteEventArgs a)
	{
		Application.Quit ();
		a.RetVal = true;
	}
}
/*
meetupGroup.id	1688379
YoungOutdoorAdventurersofOrlando

Events with pictures: 226247870,225886260
https://api.meetup.com/2/photo_albums?&sign=true&photo-host=public&group_id=1688379&event_id=226247870&page=20

8/17/2010 11:00:00 PM - Pool-Side Pot-Luck  ID=14381751
9/2/2010 1:00:00 PM - Cocoa Beach Surfing and Sea Kayaking ID=14576386
9/4/2010 7:30:00 PM - Wekiwa Springs State Park Hike/Camp/Swim.  ID=14382239
9/14/2010 11:00:00 PM - Indoor Rock Wall at Aiguille  ID=kkdhpynmbsb
9/19/2010 3:00:00 PM - A Day in the Park ID=14393862
9/25/2010 12:30:00 PM - International Coastal Cleanup ID=14824445
10/1/2010 11:00:00 PM - Florida Ranch Rodeo Finals ID=14545118
10/12/2010 11:00:00 PM - Indoor Rock Wall at Aiguille  ID=kkdhpynnbqb
10/16/2010 4:30:00 PM - Ginnie Springs Day OR overnight.  ID=14848267
10/22/2010 11:00:00 PM - Rebounderz ID=14605186
10/30/2010 3:30:00 PM - Long and Scott Farms Corn Maze ID=14804235
11/6/2010 12:00:00 PM - Walk to Cure Psoriasis - Tampa ID=14604208
11/7/2010 4:30:00 PM - ZoomAir (ziplines/obstacles in trees) ID=15317669
11/10/2010 12:00:00 AM - Indoor Rock Wall at Aiguille  ID=kkdhpynpbmb
11/13/2010 5:00:00 PM - Beach Palooza ID=14804605
11/14/2010 7:00:00 PM - Let's take a hike ID=15332426
11/30/2010 12:00:00 AM - Hard Knocks "Combat" ID=14804701
12/4/2010 12:00:00 AM - Movie in the Park- The Polar Express ID=14804941
12/10/2010 12:00:00 AM - Ice skating in Winter Park ID=15423360
12/15/2010 12:00:00 AM - Indoor Rock Wall at Aiguille  ID=kkdhpynqbsb
12/18/2010 11:00:00 PM - YOAO Social/Charity event.  ID=15406812
12/19/2010 11:30:00 PM - Christmas lights/display ID=15744178
12/25/2010 7:00:00 PM - Chrismas Hiking ID=15694335
1/2/2011 5:00:00 PM - Paintball ID=15697095
1/12/2011 12:00:00 AM - Indoor Rock Wall at Aiguille  ID=kkdhpypcbpb
1/29/2011 12:00:00 AM - Retro Game Night at History Center ID=15974909
1/29/2011 5:00:00 PM - Warrior Dash! ID=15586246
2/5/2011 3:30:00 PM - Kayaking at Brevard Zoo ID=15974648
2/12/2011 7:30:00 PM - Hontoon Island State Park Camping ID=15732686
2/19/2011 6:00:00 PM - Hiking ID=16510547
2/26/2011 5:00:00 PM - Orlando Chili Cook-Off ID=15732851
3/9/2011 12:00:00 AM - Indoor Rock Wall at Aiguille ID=kkdhpypfblb
3/20/2011 5:30:00 PM - Wekiwa Springs Day Trip ID=16872547
3/26/2011 10:30:00 PM - Parkour (also called PK) ID=16837617
4/3/2011 6:00:00 PM - Hiking, then tubing ID=16911168
4/9/2011 11:00:00 PM - Florida Music Festival ID=16890337
4/12/2011 11:00:00 PM - Indoor Rock Wall at Aiguille ID=kkdhpypgbqb
4/16/2011 2:00:00 PM - Canoeing / Kayaking ID=17271205
4/22/2011 11:00:00 PM - Orlando City Soccer Game at the Citrus Bowl! ID=17250864
4/23/2011 10:30:00 AM - 2011 Fight For Air Run/Walk 5K ID=16873455
4/30/2011 1:30:00 PM - Manatee Encounter Kayak Tour ID=16735048
4/30/2011 3:30:00 PM - Hiking and Camping ID=17438151
5/7/2011 5:00:00 PM - BEACH! ID=16873136
5/10/2011 11:00:00 PM - Indoor Rock Wall at Aiguille ID=kkdhpyphbnb
5/14/2011 12:00:00 PM - Habitat for Humanity ID=17029599
5/14/2011 7:30:00 PM - Tubing after Habitat for Humanity ID=17568438
5/15/2011 11:00:00 PM - Rockclimbing ID=cwxfryphbtb
5/21/2011 4:00:00 PM - Kayaking/Canoeing ID=18457501
5/22/2011 10:00:00 PM - Rockclimbing ID=cwxfryphbdc
5/28/2011 10:00:00 PM - Rebounderz: trampolines and FOAM dodgeball (doesn't hurt) ID=17317547
		5/29/2011 4:00:00 PM - BEACH ID=18950391
		5/29/2011 10:00:00 PM - Rockclimbing ID=cwxfryphbmc
		6/3/2011 11:00:00 PM - Retro Game Night: 10th Anniv. College Night ID=20450301
		6/4/2011 3:00:00 PM - canoe/kayak ID=19612691
		6/5/2011 10:00:00 PM - Rockclimbing ID=cwxfrypjbhb
		6/11/2011 11:00:00 PM - Ice Skating, Beginners welcome ID=19862121
		6/12/2011 2:00:00 PM - Beginner Power Yoga ID=17617821
		6/14/2011 11:00:00 PM - Indoor Rock Wall at Aiguille ID=kkdhpypjbsb
		6/18/2011 4:00:00 PM - Shooting Range and Springs Trip ID=16843374
		6/19/2011 10:00:00 PM - Rockclimbing ID=cwxfrypjbzb
		6/25/2011 3:00:00 PM - Recreational Swimming ID=20520051
		6/26/2011 10:00:00 PM - Rockclimbing ID=cwxfrypjbjc
		7/2/2011 1:00:00 AM - KARAOKE! ID=21963461
		7/3/2011 10:00:00 PM - Rockclimbing ID=cwxfrypkbfb
		7/10/2011 9:00:00 PM - Running/Walking at Cranes Roost ID=20670671
		7/12/2011 10:30:00 PM - Indoor Rock Wall at Aiguille ID=kkdhpypkbqb
		7/16/2011 2:00:00 PM - DeLeon Springs State Park ID=21446671
		7/30/2011 10:00:00 PM - ReRebounderz: trampolines and FOAM dodgeball (doesn't hurt) ID=23436681
8/9/2011 10:30:00 PM - Indoor Rock Wall at Aiguille ID=kkdhpyplbmb
8/20/2011 3:00:00 PM - Gatorland ID=25790101
9/17/2011 8:00:00 PM - Camping trip on the beach ID=18513661
9/21/2011 11:30:00 PM - Serinity Now Yoga offers Martial Arts Classes ID=33296272
9/25/2011 4:00:00 PM - Alexander Springs ID=30939821
10/11/2011 11:45:00 PM - ZoomAir full moon aerial adventure ID=34189282
10/15/2011 12:00:00 PM - The Highlander ID=32075632
10/20/2011 9:15:00 PM - Light the Night LLS Awareness Walk ID=35957472
11/5/2011 10:00:00 PM - Rebounderz: trampolines and FOAM dodgeball (doesn't hurt) ID=pjpkxyppbhb
		11/11/2011 1:00:00 PM - Becoming an Outdoors Woman (BOW) ID=37591522
		11/12/2011 7:00:00 PM - Blue Spring ID=35051382
		11/19/2011 5:30:00 PM - Ginnie Springs: camping/tubing ID=39446492
		12/3/2011 5:00:00 PM - Corn Maze ID=35923072
		12/7/2011 12:00:00 AM - Ice Skating at Light Up UCF ID=39599732
		12/10/2011 3:00:00 PM - Hiking, then farmer's market ID=39375502
12/13/2011 12:00:00 AM - Christmas Display/Light Viewing ID=41600902
1/10/2012 11:30:00 PM - Indoor Rock Wall at Aiguille ID=pwqwxyqcbnb
1/17/2012 12:00:00 AM - Laser Tag at Battleground Orlando ID=42046642
1/22/2012 5:00:00 PM - Lyonia Preserve: Hike ID=41144152
2/5/2012 11:00:00 PM - Rebounderz: trampolines and FOAM dodgeball (doesn't hurt) ID=pjpkxyqdbgb
		2/25/2012 2:30:00 PM - Biking in Winter Garden ID=47091022
		2/28/2012 12:00:00 AM - Laser Tag ID=51853992
		3/3/2012 1:30:00 PM - JDRF Annual Walk-For-The-Cure-5K FREE Event ID=50573492
		3/11/2012 2:30:00 PM - Kayaking at Brevard Zoo ID=46725762
		3/12/2012 11:00:00 PM - Laser Tag at Battleground Orlando (FPS IRL) ID=qpjjlcyqfbqb
		3/17/2012 4:00:00 PM - Alexander Springs ID=50107072
		3/19/2012 11:00:00 PM - Paintball! ID=54347902
		3/21/2012 11:00:00 PM - Run around Lake Eola ID=54200182
		3/25/2012 3:00:00 PM - Hike in Seminole State Forest, followed by tubing ID=54152502
		4/1/2012 4:00:00 PM - Wekiva Springs State Park ID=50118742
		4/7/2012 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyqgbnb
		4/18/2012 11:00:00 PM - Running/Walking at Cranes Roost ID=dptcscyqgbxb
		4/23/2012 11:00:00 PM - Laser Tag at Woods Laser Tag ID=49180962
		4/27/2012 10:00:00 PM - Relay For Life for the American Cancer Society ID=57242452
			4/29/2012 4:00:00 PM - ZoomAir (Treetop adventure) ID=58420992
				4/30/2012 11:00:00 PM - Laser Tag ID=59025162
				5/5/2012 3:00:00 PM - Explore Oakland Nature Preserve ID=58255512
				5/8/2012 10:00:00 PM - Trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyqhblb
5/20/2012 5:00:00 PM - Wakeboarding/Kneeboarding ID=57029392
5/21/2012 10:45:00 PM - Paintball! ID=58537602
5/26/2012 12:00:00 PM - Gun Range (Shooting) & Springs Trip ID=58408232
6/1/2012 11:00:00 PM - History Center Retro Game Night ID=62431552
6/2/2012 7:00:00 PM - Rock Climbing! ID=62979992
6/2/2012 10:00:00 PM - Kayak in Merritt Island ID=52717322
6/11/2012 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyqjbpb
6/20/2012 11:00:00 PM - Run around Lake Eola ID=dgbpscyqjbbc
7/7/2012 1:00:00 PM - Tubing at Kelly Park ID=70427612
7/8/2012 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyqkblb
7/9/2012 10:45:00 PM - Paintball! ID=qbgqxcyqkbmb
7/18/2012 11:00:00 PM - Run around Lake Eola ID=dgbpscyqkbxb
7/21/2012 4:00:00 PM - Lake Louisa State Park ID=69715302
7/30/2012 11:00:00 PM - Laser Tag ID=72411872
8/2/2012 11:00:00 PM - WhirlyBall @ WhirlyDome on I-Drive ID=70838612
8/15/2012 11:00:00 PM - Run around Lake Eola ID=dgbpscyqlbtb
8/18/2012 10:00:00 PM - Boing Jump Center: trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyqlbsb
				8/27/2012 10:45:00 PM - Paintball! ID=qbgqxcyqmbnb
				9/2/2012 10:30:00 PM - Zipline Eco Tour! ID=78441262
				9/5/2012 11:00:00 PM - Run around Lake Eola - 1st Wed ID=drnwgdyqmbhb
				9/8/2012 6:00:00 PM - Stand up paddleboarding ID=76737222
				9/10/2012 11:00:00 PM - Laser Tag at Battleground Orlando (FPS IRL) ID=qgshncyqmbnb
				9/15/2012 11:30:00 PM - SOLD OUT - Bioluminescent Kayaking ID=77597712
				9/19/2012 11:00:00 PM - Run around Lake Eola - 3rd Wed ID=dgbpscyqmbqb
				9/21/2012 11:00:00 PM - History Center Retro Game Night ID=77466022
				9/22/2012 1:00:00 PM - Mud Run, Highlander III! ID=83483592
				9/29/2012 4:00:00 PM - Wekiva Falls ID=79336732
				9/30/2012 5:45:00 PM - Free Ice Skating rink admission, then british pub & possibly $1 movie *Updated* ID=84240412
				10/7/2012 4:00:00 PM - Maíz Maze ID=78393052
				10/10/2012 11:00:00 PM - Run around Lake Eola - 2nd Wed ID=80466152
				10/14/2012 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyqnbsb
				10/20/2012 10:30:00 PM - Bioluminescent Kayaking Redux ID=83854652
				10/24/2012 11:00:00 PM - Run around Lake Eola - 4th Wed ID=qgdnfdyqnbwb
				11/4/2012 1:45:00 PM - horse back (trail) riding ID=86460512
				11/8/2012 12:00:00 AM - Run around Lake Eola - 1st Wed ID=drnwgdyqpbkb
				11/10/2012 12:00:00 AM - Make-A-Wish of Central and Northern Florida's Glo.Wish.Run 5k ID=83914902
11/10/2012 6:15:00 PM - Shoot Archery like Katniss! ID=78881872
11/12/2012 11:45:00 PM - Paintball! ID=qbgqxcyqpbqb
11/13/2012 11:00:00 PM - Boing Jump Center: trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyqpbrb
				11/17/2012 5:00:00 PM - CANCELED: Camping trip on the beach ID=87577822
				11/18/2012 2:00:00 PM - DeLeon Springs State Park ID=90022572
				11/22/2012 12:00:00 AM - Run around Lake Eola - 3rd Wed ID=qgdnfdyqpbcc
				11/24/2012 4:45:00 PM - camping at moss park ID=87771582
				11/25/2012 2:30:00 PM - hike split oak mitigation park ID=90994812
				12/2/2012 10:00:00 PM - indoor rock climbing at aiguille ID=90991852
				12/6/2012 12:00:00 AM - Run around Lake Eola - 1st Wed ID=drnwgdyqqbhb
				12/11/2012 12:00:00 AM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyqqbnb
				12/12/2012 11:30:00 PM - Christmas Lights/Displays ID=92406482
				12/19/2012 12:00:00 AM - ice skate (and more) @ light up UCF ID=92228032
				12/19/2012 11:00:00 PM - Run around Lake Eola - 3rd Wed ID=qgdnfdyqqbzb
				12/22/2012 5:00:00 PM - Hiking Barr Trail ID=94664472
				1/2/2013 11:00:00 PM - Run around Lake Eola - Happy New Year Edition ID=drnwgdyrcbdb
				1/5/2013 6:00:00 PM - Sailing on the Banana River ID=94272732
				1/8/2013 12:00:00 AM - Laser Tag ID=95833542
				1/13/2013 10:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyrcbrb
				1/14/2013 11:45:00 PM - Paintball! ID=qbgqxcyrcbsb
				1/17/2013 12:00:00 AM - Run around Lake Eola - 3rd Wed ID=qrnwzdyrcbvb
				1/27/2013 1:00:00 PM - Prekayaking/Precanoeing ID=92072992
				1/27/2013 6:00:00 PM - Kayaking/Canoeing ID=92052782
				2/2/2013 7:00:00 PM - Shoot archery like Gale (not as well as Katnis, but not too shabby, nonetheless) ID=92627992
				2/7/2013 12:00:00 AM - Run around Lake Eola - 1st Wed ID=drnwgdyrdbjb
				2/10/2013 5:00:00 PM - Go biking! And then get lunch. ID=103604562
				2/12/2013 11:00:00 PM - Trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyrdbqb
2/14/2013 11:30:00 PM - Forget the candy and flowers and spend the 14th rock climbing at Aiguille ID=104369372
2/17/2013 6:00:00 PM - Hiking Lake Jessup Trail ID=94667032
2/21/2013 12:00:00 AM - Run around Lake Eola - 3rd Wed ID=qrnwzdyrdbbc
2/24/2013 1:00:00 PM - 8.5-Mile Kayak Trip ID=92074702
3/2/2013 5:00:00 PM - Hiking at Wekiva State Park (Intermediate skill level) ID=99234672
3/5/2013 12:00:00 AM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyrfbpb
3/7/2013 12:00:00 AM - Run around Lake Eola - 1st Wed ID=drnwgdyrfbjb
3/7/2013 11:00:00 PM - Bounce for Charity! - Trampolines and FOAM dodgeball (doesn't hurt) ID=104409682
				3/9/2013 3:00:00 PM - Home tour of 96 year old inventor Jacque Fresco! Update Feb. 12th: Sold out ID=93902002
				3/10/2013 1:30:00 PM - Paddle the Silver River (and see Wild monkeys in Florida) ID=103249562
				3/11/2013 10:45:00 PM - Paintball! ID=qbgqxcyrfbpb
				3/22/2013 1:30:00 AM - Lets meet every Thurs and do some fun bachata dancing(like merengue, but funner) ID=107755982
				3/27/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrfbbc
				3/29/2013 1:30:00 AM - Lets meet every Thurs and do some fun bachata dancing(like merengue, & Free ID=110684782
				                                                                             3/29/2013 9:45:00 PM - Critical Mass- biking event ID=108757302
				                                                                             3/30/2013 4:00:00 PM - Vegetarian Potluck with 100% solar car demonstration *Updated ID=109011982
				                                                                             4/1/2013 11:00:00 PM - Laser Tag ID=108111942
				                                                                             4/5/2013 11:15:00 PM - Gatorland: After hours guided tour and night feeding ID=108751602
				                                                                             4/6/2013 8:00:00 PM - Camping: Weeki Wachee ID=104755512
				                                                                             4/7/2013 1:00:00 PM - Weeki Wachee Springs ID=92211382
				                                                                             4/10/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrgbnb
				                                                                             4/13/2013 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyrgbsb
				                                                                             4/14/2013 12:30:00 PM - Rock Springs Run to Wekiva Island (Kayak/Canoe) ID=111681662
				                                                                             4/19/2013 11:00:00 PM - Seminole State Forest Camping ID=111500752
				                                                                             4/20/2013 11:00:00 PM - drive in movie theater!! Oblivion & G I Joe2 for 4$ ID=86534232
				                                                                             4/24/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrgbwb
				                                                                             4/27/2013 1:00:00 PM - Spoil Island Camping ID=112749902
				                                                                             5/4/2013 12:30:00 PM - Tubing at Kelly Park ID=116998242
				                                                                             5/8/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrhblb
				                                                                             5/11/2013 2:00:00 PM - Wallaby Ranch for swimming pool, volleyball, Hang Gliding, & more ID=116210782
				                                                                             5/12/2013 10:00:00 PM - Indoor Rock Wall at Aiguille ID=114365442
				                                                                             5/18/2013 5:30:00 PM - Wakeboarding/Kneeboarding ID=92076892
				                                                                             5/22/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrhbdc
				                                                                             5/31/2013 9:45:00 PM - Critical Mass- biking event ID=119438422
				                                                                             6/5/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrjbhb
				                                                                             6/9/2013 2:00:00 PM - Mountain Biking Snow Hill ID=16300905
				                                                                             6/10/2013 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyrjbnb
				                                                                             6/15/2013 12:45:00 PM - Mangrove Tunnels of Weedon Island (Kayak) ID=111690182
				                                                                             6/19/2013 11:15:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrjbzb
				                                                                             6/22/2013 1:00:00 PM - Biathlon - Hiking, then swimming ID=116584342
				                                                                             6/23/2013 2:00:00 PM - Grill out at the Beach! ID=125573562
				                                                                             6/28/2013 9:45:00 PM - Critical Mass- biking event ID=124977682
				                                                                             6/29/2013 12:00:00 PM - Tubing at Ichetucknee Springs ID=112569502
				                                                                             7/3/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrkbfb
				                                                                             7/17/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrkbwb
				                                                                             7/20/2013 12:30:00 PM - Tubing in the Rainbow River ID=122108222
				                                                                             7/21/2013 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyrkbsb
				                                                                             7/25/2013 11:00:00 PM - Urban Assault Mountain Biking ID=126409782
				                                                                             7/27/2013 11:00:00 PM - Figure skating ID=127302232
				                                                                             7/31/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrkbpc
				                                                                             8/3/2013 1:00:00 PM - Weeki Wachee Springs Kayaking ID=130243682
				                                                                             8/4/2013 12:30:00 PM - Outdoor Yoga for a cause (Karma Yoga) ID=129454482
				                                                                             8/10/2013 12:00:00 PM - Tubing at Kelly Park ID=129322332
				                                                                             8/11/2013 6:30:00 PM - Karma Yoga - Afternoon edition! ID=132999132
				                                                                             8/13/2013 10:00:00 PM - Boing Jump Center: trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyrlbrb
8/14/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrlbsb
8/18/2013 12:30:00 PM - Karma Yoga - Morning edition! ID=dlxfngyrlbxb
8/18/2013 9:30:00 PM - Indoor Rock Wall at Aiguille ID=130903192
8/24/2013 7:00:00 AM - Hike the Humps in Roan Highlands Tennessee ID=129362512
8/28/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrlblc
8/31/2013 1:00:00 PM - Biking in Winter Garden ID=129265562
8/31/2013 11:15:00 PM - Bioluminescent Kayaking ID=133179402
9/1/2013 12:00:00 PM - Pool party at Wallaby Ranch + optional Hang Gliding flight for only $99! ID=129688642
9/8/2013 12:30:00 PM - Rainbow River Reloaded (4-hour tubing run) ID=131809072
9/9/2013 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyrmbmb
9/11/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrmbpb
9/14/2013 1:00:00 PM - Trail biking the Santos, Ocala Forest ID=131425022
9/15/2013 12:30:00 PM - Karma Yoga - Morning edition! ID=dlxfngyrmbtb
9/15/2013 9:45:00 PM - Indoor Rock Wall at Aiguille ID=138416982
9/21/2013 1:30:00 PM - SUP, Tandem Kayak, and Hydrobikes ID=136784452
9/22/2013 11:00:00 AM - The Color Run 5K Orlando @ The Citrus Bowl ID=122919092
9/25/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrmbhc
9/28/2013 1:00:00 PM - Trail biking the swamp at Alafia ID=131438802
9/29/2013 12:30:00 PM - Karma Yoga - Morning edition! ID=dlxfngyrmbmc
9/29/2013 11:30:00 PM - Rollerskating ID=141407842
10/5/2013 12:00:00 PM - Pool party at Wallaby Ranch + optional Hang Gliding special ID=138478232
10/9/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrnbmb
10/12/2013 11:30:00 AM - Armageddon Ambush: The Extreme Mud & Color Run ID=127864202
10/13/2013 12:30:00 PM - Karma Yoga - Morning edition! ID=dlxfngyrnbrb
10/20/2013 1:00:00 PM - Ocean Sailing Trip ID=140594162
10/23/2013 11:00:00 PM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrnbfc
10/24/2013 10:00:00 PM - Halloween Hustle 5K ID=135256192
10/25/2013 11:30:00 PM - A Petrified Forest (Haunted Forest) ID=142061922
10/27/2013 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyrnbrb
11/2/2013 11:00:00 PM - Close-enough-to-Halloween Night Hike - Barr Trail ID=144656912
11/7/2013 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrpbjb
11/9/2013 5:00:00 PM - Maíz Maze ID=141549462
11/12/2013 11:00:00 PM - Boing Jump Center: trampolines and FOAM dodgeball (doesn't hurt) ID=dtwkngyrpbqb
				                                                                             11/17/2013 10:30:00 PM - Indoor Rock Wall at Aiguille ID=147857562
				                                                                             11/21/2013 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrpbbc
				                                                                             11/24/2013 5:00:00 PM - Wekiva Springs, now with more PM (sleeping in) ID=147322242
				                                                                             11/30/2013 4:00:00 PM - Biking in Winter Garden ID=152232802
				                                                                             12/5/2013 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrqbgb
				                                                                             12/7/2013 3:00:00 PM - Trail biking the swamp at Alafia ID=148315442
				                                                                             12/8/2013 11:00:00 PM - ice skate (and more) @ light up UCF ID=152574722
				                                                                             12/10/2013 12:30:00 AM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncyrqbmb
				                                                                             12/14/2013 6:00:00 PM - Camping at moss park 2nd annual --full moon--almost ID=147852572
				                                                                             12/17/2013 11:30:00 PM - Christmas displays/lights ID=155077942
				                                                                             12/19/2013 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=qrnwzdyrqbxb
				                                                                             12/21/2013 1:00:00 PM - Habitat for Humanity ID=146773972
				                                                                             12/22/2013 5:00:00 PM - Volunteering with ReStore ID=154426552
				                                                                             12/25/2013 7:45:00 PM - Christmas Day Chinese Food & The Wolf of Wall Street based on my friend Jordan. ID=155424192
				                                                                             12/27/2013 10:45:00 PM - Critical Mass ID=qgdrbhyrqbkc
				                                                                             1/1/2014 1:00:00 AM - NYE 2014 Block Party @ Liam Fitzpatrick's in Lake Mary with a shamrock drop @12 ID=157250742
1/4/2014 6:00:00 PM - Shoot archery like Merida ID=155476832
1/5/2014 4:45:00 PM - Ladies Only Event-Learn Silks at Vixen ID=158071722
1/11/2014 1:00:00 PM - Historical Hangout at De Leon Springs ID=158536472
1/16/2014 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=qrnwzdyscbtb
1/18/2014 2:00:00 PM - Habitat five Humanity YES LIST CHECK UR EMAIL ID=149595862
1/19/2014 3:00:00 PM - Scottish Highland Games Winter Springs, FL ID=158080402
1/25/2014 1:00:00 AM - Co-Ed Night @ Vixen Fitness  ID=161236062
1/30/2014 12:00:00 AM - CANCELED BY WEATHER - Run around Lake Eola ID=qrnwzdyscbmc
1/31/2014 10:45:00 PM - Critical Mass ID=qgdrbhyscbpc
2/1/2014 4:00:00 PM - 2014 Warrior Dash ID=148300072
2/1/2014 4:45:00 PM - LADIES ONLY! Intro to Pole Dancing Class ID=158602382
2/1/2014 11:15:00 PM - Volunteer - Give Kids the World - Full ID=151774822
2/2/2014 1:00:00 PM - Obstacle "Training" for a Mud Run ID=161388592
2/6/2014 12:00:00 AM - Journey Through Love-Fairvilla Valentines Party-Free ID=164019452
2/8/2014 4:45:00 PM - LADIES ONLY-Intro to Pole Dance Class  ID=164838552
2/13/2014 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=drqjshysdbqb
2/13/2014 11:30:00 PM - Airheads:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhysdbpb
				2/15/2014 12:30:00 AM - Burlesque party for everyone and anyone!  ID=162957252
					2/15/2014 5:00:00 PM - Cupid Undie Run!  ID=164017672
						2/23/2014 4:00:00 PM - Wetsuit n' Wild ID=160615792
2/27/2014 12:00:00 AM - Run around Lake Eola - Alternating Wed ID=drqjshysdbjc
3/2/2014 1:00:00 PM - FREE Obstacle Training/Fun Day ID=165839532
3/8/2014 1:30:00 PM - JDRF Charity Walk 2014 (Walk to cure Diabetes) FREE EVENT ID=161735362
3/8/2014 2:00:00 PM - Habitat 4  Humanity Wp ID=164564432
3/10/2014 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncysfbnb
3/12/2014 11:00:00 PM - CANCELED: Run around Lake Eola - Alternating Wed ID=drqjshysfbqb
3/16/2014 5:00:00 PM - Shoot archery like Orlando Bloom ID=161845472
3/22/2014 1:00:00 PM - Kayak Rock Springs Run ID=164043392
3/23/2014 11:00:00 PM - Close-enough-to-Halloween Night Hike - Barr Trail ID=165870132
3/28/2014 9:45:00 PM - Critical Mass ID=qgdrbhysfblc
3/29/2014 3:00:00 AM - rocky horror picture show ID=170971752
4/1/2014 5:00:00 PM - Snorkeling in Lake Jessup ID=152950372
4/14/2014 3:00:00 PM - Indoor Rock climbing ID=175952402
4/16/2014 1:00:00 AM - Co-ed Pole or Silks Class $10.00 for first timers ID=177049532
4/19/2014 1:00:00 PM - Weeki Wachee Springs Kayaking ID=173573012
4/20/2014 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcysgbrb
4/21/2014 10:45:00 PM - Paintball! ID=174953902
4/23/2014 1:00:00 AM - Co-ed Pole or Silks Class  ID=177049742
4/25/2014 9:45:00 PM - Critical Mass ID=qgdrbhysgbhc
4/26/2014 2:45:00 PM - MONSTER CHALLENGE ID=152132392
4/26/2014 11:00:00 PM - Florida Music Festival (outdoor concert) ID=160428832
5/3/2014 12:00:00 PM - Pool party at Wallaby Ranch + optional Hang Gliding special ID=159131532
5/13/2014 11:30:00 PM - Rebounderz:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhyshbrb
						5/18/2014 9:30:00 PM - Indoor Rock Wall at Aiguille ID=178292352
						5/24/2014 12:30:00 PM - Rainbow River (4-hour tubing run) ID=176935072
						5/25/2014 5:00:00 PM - Shoot archery like Cupid ID=172485322
						5/30/2014 9:45:00 PM - Critical Mass ID=qgdrbhyshbnc
						5/31/2014 12:00:00 PM - Habitat for Humanity ID=159095592
							6/1/2014 5:45:00 PM - Ice Skating in June @ RDV  ID=182541872
								6/2/2014 10:45:00 PM - Paintball! ID=178646882
								6/9/2014 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncysjbmb
								6/14/2014 2:00:00 PM - Blackberry picking ID=184883762
								6/22/2014 1:00:00 AM - Stigma Bar Downtown Orlando ID=185193982
								6/27/2014 9:45:00 PM - Critical Mass ID=qgdrbhysjbkc
								6/29/2014 1:30:00 PM - Kickball and shenanigans ID=184882782
								7/12/2014 12:00:00 PM - Rainbow River (4-hour tubing run) ID=185254182
								7/13/2014 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcyskbrb
								7/19/2014 1:00:00 PM - Clean the World Volunteering - Sorting and Packing ID=186536382
								7/25/2014 9:45:00 PM - Critical Mass ID=qgdrbhyskbhc
								7/25/2014 11:00:00 PM - Indoor Rockclimbing ID=196156162
								7/26/2014 11:15:00 PM - Sold out - Bioluminescent Kayaking ID=175251982
								8/2/2014 3:00:00 PM - Volunteer at Harbor House ID=187018942
								8/3/2014 2:00:00 PM - Paddleboarding ID=188441692
								8/9/2014 11:00:00 AM - Triathlon-mini me style ID=184883392
								8/9/2014 12:00:00 PM - Habitat for Humanity - Painting ID=187235392
									8/17/2014 9:00:00 PM - Indoor Rock Climbing Aiguille ID=197495622
										8/29/2014 9:45:00 PM - Critical Mass ID=qgdrbhyslbmc
										8/31/2014 11:00:00 PM - Coffee run at Crane's Roost ID=199873512
9/4/2014 12:00:00 AM - test our wits at the great escape room challenge ID=203163382
9/7/2014 5:00:00 PM - Shoot archery like Phil Graves ID=185782322
9/8/2014 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncysmblb
9/13/2014 1:00:00 PM - Hit and run 5k ID=205263262
9/13/2014 4:00:00 PM - 2nd Annual Pedal and Poker for Charity on Sept. 13 ID=206319462
9/20/2014 11:00:00 PM - Chile & Patagonia Adventure! ID=192407332
9/26/2014 9:45:00 PM - Critical Mass ID=qgdrbhysmbjc
9/27/2014 12:00:00 PM - Rainbow River (4-hour tubing run) ID=200015762
9/28/2014 9:30:00 PM - Indoor Rock Wall at Aiguille ID=200919542
10/2/2014 11:30:00 PM - Bumpercar Lacrosse Basketball (aka WhirlyBall) ID=203177542
10/12/2014 9:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcysnbqb
10/27/2014 11:00:00 PM - 2 Hr Trapeze Class ID=212113952
10/28/2014 10:00:00 PM - October Tuesday paintball special...a rare and great deal! ID=213257112
10/30/2014 11:30:00 PM - A Petrified Forest (Haunted Forest) ID=212759072
10/31/2014 9:45:00 PM - Critical Mass ID=qgdrbhysnbpc
11/1/2014 1:00:00 AM - Rocky Horror Picture Show-Halloween Edition  ID=216630972
11/8/2014 2:30:00 PM - Eat le French food @ My French Cafe in Windermere ID=217669252
11/12/2014 12:00:00 AM - Airheads:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhyspbpb
										11/15/2014 2:15:00 PM - Manatee Swim at Crystal River - Read description before RSVPing ID=216322982
										11/15/2014 4:45:00 PM - Ladies Only-Pole Fitness Class ID=218626205
										11/16/2014 1:00:00 PM - Come over and Enjoy giant tasty PumpkinCake  ID=218709165
										11/16/2014 10:45:00 PM - Indoor Rock Wall at Aiguille...sorry for the short notice ID=218679167
											11/22/2014 6:45:00 PM - ICE Skating! @ RDV Sportplex  ID=218755655
												11/23/2014 6:00:00 PM - Shoot archery like Link ID=212104892
												11/28/2014 6:00:00 PM - Rock Climbing at Aiguille! ID=218891996
												11/28/2014 10:45:00 PM - Critical Mass ID=qgdrbhyspblc
11/29/2014 5:00:00 PM - Maíz Maze ID=212316232
12/6/2014 6:00:00 PM - Camping at moss park 3rd annual --full moon-- ID=200916752
12/6/2014 8:00:00 PM - Learn How to Shoot Handguns ID=218933066
12/7/2014 3:01:00 PM - intermediate hike; moss park/split oak mitigation park ID=200926042
12/13/2014 11:00:00 PM - Free Amazing Christmas lights ID=219161879
12/16/2014 12:00:00 AM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncysqblb
12/19/2014 11:30:00 PM - Holiday Jazz@Cranes Roost Eddie Rose Amphitheater ID=219269652
12/20/2014 3:00:00 PM - Let's go hiking in Alafia River State Park ID=219003774
12/25/2014 6:00:00 PM - Christmas Dinner with the homeless of Sanford ID=219269744
12/26/2014 10:45:00 PM - Critical Mass ID=qgdrbhysqbjc
12/28/2014 3:15:00 PM - Hula hoop workout class ID=219450518
12/30/2014 3:00:00 PM - NRA 8 Hour Basic Pistol Shooting Course ID=219372215
1/7/2015 1:00:00 AM - Co-Ed Hip Hop and/or Aerial Silks Class ID=219489597
1/10/2015 12:00:00 AM - Outdoor Ice-Skating ID=218833155
1/10/2015 4:45:00 PM - Ladies Only-Intro to Pole Fitness Class ID=219692275
1/11/2015 10:00:00 PM - Indoor Rock Wall at Aiguille ID=dbbllcytcbpb
1/17/2015 12:45:00 PM - The Chocolate 5K for The Kerosene Lamp Foundation ID=219332456
1/17/2015 1:00:00 PM - Habitat for Humanity ID=219355143
1/17/2015 5:00:00 PM - Scottish Highland Games ID=219332388
1/21/2015 2:00:00 AM - Co-Ed Aerial Silks ID=219489674
1/23/2015 1:00:00 AM - Ladies only-aerial silks class ID=219692332
1/24/2015 1:00:00 PM - Habitat for Humanity ID=219355147
1/30/2015 10:45:00 PM - Critical Mass ID=qgdrbhytcbnc
1/31/2015 4:45:00 PM - Ladies Only-Pole Fitness Class ID=220187309
2/1/2015 6:00:00 PM - Shoot archery like Sarah Palin ID=219388896
2/7/2015 2:00:00 PM - Kayak Rock Springs Run to Wekiva Island ID=219638007
2/11/2015 12:00:00 AM - Rebounderz:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhytdbnb
2/21/2015 3:00:00 PM - Co-ed NRA 8 Hour Basic Pistol Shooting Course ID=220253254
2/22/2015 10:00:00 PM - Indoor Rock Wall at Aiguille ID=220591189
2/27/2015 10:45:00 PM - Critical Mass ID=qgdrbhytdbkc
2/28/2015 3:00:00 PM - Ladies-only NRA 8 Hour Basic Pistol Shooting Course ID=220253306
3/1/2015 12:00:00 AM - SAK comedy lab ID=220826562
3/7/2015 12:00:00 AM - The Big Bang Theory Themed Birthday Party ID=220884987
3/9/2015 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncytfbmb
3/14/2015 12:00:00 PM - Habitat for Humanity ID=220292353
3/14/2015 1:00:00 PM - Do BattleFrog Obstacle Race! ID=210246522
3/14/2015 1:00:00 PM - Canoe Camping Trip on the Suwannee River  ID=220753578
3/21/2015 12:00:00 PM - Habitat for Humanity ID=220292362
3/21/2015 2:00:00 PM - Silver River State Park Kayaking and Hiking ID=220168482
3/21/2015 7:00:00 PM - Tubing at Kelly Park (following Habitat for Humanity) ID=221225595
3/22/2015 3:00:00 PM - Picnic/Farmers Market @ Lake Eola Park ID=221176467
3/27/2015 9:45:00 PM - Critical Mass ID=qgdrbhytfbkc
3/28/2015 2:30:00 AM - Rocky Horror Picture Show ID=221343542
4/12/2015 5:00:00 PM - Shoot archery like Robin Hood ID=220470988
4/18/2015 1:00:00 PM - Pick Blueberries @ Tom West Bluberry farm ID=221176724
4/20/2015 11:00:00 PM - Paintball! ID=221510581
4/24/2015 9:45:00 PM - Critical Mass ID=qgdrbhytgbgc
4/25/2015 12:30:00 PM - VCI Cyber Seniors Needs Volunteers (RSVP before noon on Friday, April 24. 2015) ID=222006698
4/25/2015 10:00:00 PM - Laser Tag at Battleground Orlando ID=220292200
4/26/2015 9:00:00 PM - Indoor Rock Wall at Aiguille ID=221915359
5/8/2015 11:00:00 PM - Underwater Hockey ID=221743084
5/12/2015 11:00:00 PM - Rebounderz:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhythbqb
5/23/2015 12:00:00 PM - Habitat for Humanity (to be followed by tubing at Kelly Park) ID=222300957
5/29/2015 9:45:00 PM - Critical Mass ID=qgdrbhythbmc
6/6/2015 12:00:00 PM - Habitat for Humanity ID=219355177
6/8/2015 11:00:00 PM - Murder Mondays - Laser Tag at Battleground Orlando ID=qgshncytjblb
6/14/2015 4:30:00 PM - Full - Paddleboard from Crystal River to nearby springs (followed by snorkeling) ID=222530744
6/26/2015 9:45:00 PM - Critical Mass ID=qgdrbhytjbjc
6/27/2015 1:00:00 PM - Snorkling, kayaking/canoeing, and possibly scuba diving at Alexander springs ID=222163224
7/4/2015 1:00:00 PM - Volunteering at ReStore, followed by hiking ID=223200775
7/19/2015 5:00:00 PM - Shoot archery like Nightwolf ID=222045087
7/31/2015 9:45:00 PM - Critical Mass ID=qgdrbhytkbpc
8/2/2015 5:30:00 PM - Ice Skating ID=224239583
8/6/2015 4:30:00 PM - Pistol Safety/Shooting Lesson - Great for Beginners! ID=223872884
8/11/2015 11:00:00 PM - Rebounderz:  Trampolines and FOAM dodgeball (doesn't hurt) ID=dfmcbhytlbpb
8/28/2015 9:45:00 PM - Critical Mass ID=qgdrbhytlblc
9/3/2015 2:00:00 PM - Pistol Safety/Shooting Lesson - Great for Beginners! ID=224682958
9/16/2015 11:00:00 PM - Painting With A Purpose - Mission to Cambodia ID=224739076
9/19/2015 1:00:00 PM - Rainbow River (4-hour tubing run) ID=224967296
9/25/2015 9:45:00 PM - Critical Mass ID=qgdrbhytmbhc
9/26/2015 4:30:00 PM - Pistol Safety/Shooting Lesson - Great for Beginners! ID=225280152
10/4/2015 2:00:00 PM - Biking in Winter Garden, followed by lunching - Beginner friendly ID=225120481
10/11/2015 2:00:00 PM - Hiking in Seminole State Forest ID=225194588
10/17/2015 12:00:00 PM - Habitat for Humanityish - Exterior home repair and painting ID=225507947
10/23/2015 4:30:00 PM - Pistol Course - October Special, Prepare for the Zombies! ID=226152294
10/23/2015 11:00:00 PM - Underwater Hockey - Beginner friendly ID=225234145
10/24/2015 11:30:00 PM - A Petrified Forest (Haunted Forest) ID=225651839
10/30/2015 9:45:00 PM - Critical Mass ID=qgdrbhytnbnc
10/31/2015 11:00:00 PM - Halloween Night Hike - Barr Trail (followed by Halloween camp fire) ID=225346389
11/1/2015 10:30:00 PM - Figure skating (as in figuring out how to skate) ID=225886350
11/7/2015 1:00:00 PM - Habitat for Humanity ID=225587971
11/8/2015 3:30:00 PM - Ultimate Frisbee @ Blue Jacket Park ID=226502253
11/8/2015 10:00:00 PM - Indoor Rock Wall at Aiguille ID=225886260
11/11/2015 12:00:00 AM - Rebounderz: Trampolines and FOAM dodgeball (doesn't hurt) ID=224606721
11/17/2015 12:00:00 AM - Murder Mondays - Laser Tag at Battleground Orlando ID=225728808
11/27/2015 10:45:00 PM - Critical Mass ID=qgdrbhytpbkc
12/19/2015 5:00:00 PM - Hiking in Black Bear Trail ID=226247870
12/25/2015 10:45:00 PM - Critical Mass ID=qgdrbhytqbhc
12/27/2015 10:00:00 PM - rock climbing ID=227619952
1/4/1902 4:31:44 PM - Boing Jump Center: trampolines and FOAM dodgeball (doesn't hurt) ID=ddwgncyxcdbmb
*/