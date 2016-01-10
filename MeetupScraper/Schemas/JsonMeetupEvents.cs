public class JsonMeetupEvents
{
	public MeetupEvent [] results;
	public class MeetupEvent
	{
		public int utc_offset;
		public int headcount;
		public string visibility;
		public int waitlist_count;
		public long created;
		public class Rating
		{
			public int count;
			public double average;
		}
		public Rating rating;
		public int maybe_rsvp_count;
		public string description;
		public string event_url;
		public int yes_rsvp_count;
		public string name;
		public string id;
		public long time;
		public long updated;
		public class MeetupGroup
		{
			public string join_mode;
			public long created;
			public string name;
			public double group_lon;
			public int id;
			public string urlname;
			public double group_lat;
			public string who;
		}
		public MeetupGroup group;
		public string status;
	}
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