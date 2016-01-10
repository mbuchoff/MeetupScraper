public class JsonMeetupPhoto
{
	public string id;
	public string highres_link;
	public string photo_link;
	public string thumb_link;
	public string link;
	public long created;
	public long updated;
	public int utc_offset;
	public class Member
	{
		public int id;
		public string name;
		public string bio;
		public class Photo
		{
			public int id;
			public string highres_link;
			public string photo_link;
			public string thumb_link;
		}
		public Photo photo;

		public class PhotoAlbum
		{
			public int id;
			public string title;
			//			public class Event
			//			{
			//				public string id;
			//				public string name;
			//				public int yes_rsvp_count;
			//				public long time;
			//				public int utc_offset;
			//			}
			//			Event event;
		}
		public PhotoAlbum photo_album;
	}

	public Member member;
}