using System;

namespace MeetupScraper
{
	public class JsonPhotoAlbums
	{
		public class PhotoAlbum
		{
			public int photo_album_id;
			public int photo_count;
			public string event_id;
			public int group_id;
			public long created;
			public string link;
			public string title;
			public class Photo
			{
				public string highres_link;
				public int photo_id;
				public string photo_link;
				public string thumb_link;
			}
			public Photo album_photo;
			public long updated;
		}
		public PhotoAlbum [] results;
		public class Meta
		{
			public string next;
			public string method;
			public int total_count;
			public string link;
			public int count;
			public string description;
			public string lon;
			public string title;
			public string url;
			public string id;
			public long updated;
			public string lat;
		}
		public Meta meta;
	}
}

