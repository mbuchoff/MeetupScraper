public class JsonMeetupGroup
{
	public int id;
	public string name;
	public string link;
	public string urlname;
	public string description;
	public long created;
	public string city;
	public string country;
	public string state;
	public string join_mode;
	public string visibility;
	public double lat, lon;
	public int members;
	public class MeetupMember
	{
		public int id;
		public string name;
		public string bio;
		public MeetupPhoto photo;
	}
	public class MeetupPhoto
	{
		public int id;
		public string highres_link;
		public string photo_link;
		public string thumb_link;
	}
	public MeetupMember organizer;
	public string who;
	public MeetupPhoto group_photo;
	public string timezone;
	public class MeetupEvent
	{
		public string id;
		public string name;
		public int yes_rsvp_count;
		public long time;
		public int utc_offset;
	}
	public MeetupEvent nextEvent;
	public MeetupCategory category;
	public class MeetupCategory
	{
		public int id;
		public string name;
		public string shortname;
	}
	public MeetupPhoto [] photos;
}