using AudioBand.AudioSource;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml;
using Image = System.Drawing.Image;
using Timer = System.Timers.Timer;

namespace SonosAudioSource
{
	public class SonosAudioSource : IAudioSource
	{
		public string Name => "Sonos";

		public IAudioSourceLogger Logger { get; set; }

#pragma warning disable 00067 // The event is never used
		public event EventHandler<SettingChangedEventArgs> SettingChanged;
#pragma warning restore 00067 // The event is never used
		public event EventHandler<TrackInfoChangedEventArgs> TrackInfoChanged;
		public event EventHandler<bool> IsPlayingChanged;
		public event EventHandler<TimeSpan> TrackProgressChanged;
		public event EventHandler<float> VolumeChanged;
		public event EventHandler<bool> ShuffleChanged;
		public event EventHandler<RepeatMode> RepeatModeChanged;
		
		private readonly Timer _checkSonosTimer = new Timer(1000);
		private HttpClient _httpClient = new HttpClient();
		private RepeatMode _repeatMode;
		private float _volume;
		private bool _shuffle;
		private bool _isActive;
		private bool _isPlaying;
		private string _currentId;
		private string _clientIp;
		private string _clientPort;
		private readonly string _defaultClientPort = "1400";

		[AudioSourceSetting("Sonos IP")]
		public string ClientIp { get => _clientIp; set => _clientIp = value; }

		[AudioSourceSetting("Sonos Port (Default: 1400)")]
		public string ClientPort { get => _clientPort; set => _clientPort = value; }

		public SonosAudioSource()
		{
			_checkSonosTimer.AutoReset = false;
			_checkSonosTimer.Elapsed += CheckSonosTimerOnElapsed;
		}

		public Task ActivateAsync()
		{
			_isActive = true;
			_checkSonosTimer.Start();

			return Task.CompletedTask;
		}

		public Task DeactivateAsync()
		{
			_isActive = false;
			_checkSonosTimer.Stop();

			return Task.CompletedTask;
		}

		public Task NextTrackAsync()
		{
			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#Next\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:Next xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Speed>1</Speed>\n" +
					"		</u:Next>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		public Task PauseTrackAsync()
		{
			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#Pause\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:Pause xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Speed>1</Speed>\n" +
					"		</u:Pause>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		public Task PlayTrackAsync()
		{
			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#Play\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:Play xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Speed>1</Speed>\n" +
					"		</u:Play>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		public Task PreviousTrackAsync()
		{
			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#Previous\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:Previous xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Speed>1</Speed>\n" +
					"		</u:Previous>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		public Task SetPlaybackProgressAsync(TimeSpan newProgress)
		{
			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#Seek\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:Seek xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Unit>REL_TIME</Unit>\n" +
					"			<Target>" + newProgress.ToString("c") + "</Target>\n" +
					"		</u:Seek>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		public Task SetRepeatModeAsync(RepeatMode newRepeatMode)
		{
			_repeatMode = newRepeatMode;
			SetPlayMode();

			return Task.CompletedTask;
		}

		public Task SetShuffleAsync(bool shuffleOn)
		{
			_shuffle = shuffleOn;
			SetPlayMode();

			return Task.CompletedTask;
		}

		private void SetPlayMode()
		{
			string playMode;
			if (_repeatMode == RepeatMode.RepeatTrack)
			{
				playMode = "REPEAT_ONE";
			}
			else if (_shuffle)
			{
				playMode = "SHUFFLE";
			}
			else if (_repeatMode == RepeatMode.RepeatContext)
			{
				playMode = "REPEAT_ALL";
			}
			else
			{
				playMode = "NORMAL";
			}

			DoWebRequest("/MediaRenderer/AVTransport/Control",
					"\"urn:schemas-upnp-org:service:AVTransport:1#SetPlayMode\"",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:SetPlayMode xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<NewPlayMode>" + playMode + "</NewPlayMode>\n" +
					"		</u:SetPlayMode>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");
		}

		public Task SetVolumeAsync(float newVolume)
		{
			DoWebRequest("/MediaRenderer/RenderingControl/Control",
					"urn:schemas-upnp-org:service:RenderingControl:1#SetVolume",
					"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
					"	<s:Body>\n" +
					"		<u:SetVolume xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\">\n" +
					"			<InstanceID>0</InstanceID>\n" +
					"			<Channel>Master</Channel>\n" +
					"			<DesiredVolume>" + (newVolume * 100).ToString() + "</DesiredVolume>\n" +
					"		</u:SetVolume>\n" +
					"	</s:Body>\n" +
					"</s:Envelope>");

			return Task.CompletedTask;
		}

		private void CheckSonosTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
		{
			if (!_isActive || string.IsNullOrEmpty(_clientIp))
			{
				return;
			}

			GetCurrentTransportState();
			GetCurrentPlayMode();
			GetCurrentTrackInfoAsync();
			GetCurrentVolume();

			_checkSonosTimer.Enabled = true;
		}

		private string DoWebRequest(string url, string soapaction, string xmlData)
		{
			try
			{
				HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create("http://" + _clientIp + ":" + (string.IsNullOrEmpty(_clientPort) ? _defaultClientPort : _clientPort) + url);
				webRequest.Headers.Add("SOAPACTION", soapaction);
				webRequest.Method = "POST";
				webRequest.Timeout = 1000;
				webRequest.ContentType = "text/xml";

				string postData = xmlData;
				byte[] byteArray = Encoding.UTF8.GetBytes(postData);
				webRequest.ContentLength = byteArray.Length;

				Stream dataStream = webRequest.GetRequestStream();
				dataStream.Write(byteArray, 0, byteArray.Length);
				dataStream.Close();

				WebResponse response = webRequest.GetResponse();
				dataStream = response.GetResponseStream();
				StreamReader reader = new StreamReader(dataStream);
				string responseFromServer = reader.ReadToEnd();

				reader.Close();
				response.Close();
				return responseFromServer;
			}
			catch (Exception e)
			{
				Logger.Error(e);
				return e.ToString();
			}
		}

		private async void GetCurrentTrackInfoAsync()
		{
			string response = DoWebRequest("/MediaRenderer/AVTransport/Control",
									"\"urn:schemas-upnp-org:service:AVTransport:1#GetPositionInfo\"",
									"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
									"	<s:Body>\n" +
									"		<u:GetPositionInfo xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
									"			<InstanceID>0</InstanceID>\n" +
									"		</u:GetPositionInfo>\n" +
									"	</s:Body>\n" +
									"</s:Envelope>");

			TrackInfoChangedEventArgs trackInfo = new TrackInfoChangedEventArgs();
			TimeSpan trackProgress = new TimeSpan();
			string albumArtUri = "";
			try
			{
				XmlDocument responseXML = new XmlDocument();
				responseXML.LoadXml(response);

				trackProgress = TimeSpan.Parse(responseXML.SelectNodes("//RelTime").Item(0)?.InnerText ?? "");
				trackInfo.TrackLength = TimeSpan.Parse(responseXML.SelectNodes("//TrackDuration").Item(0)?.InnerText ?? "");

				string trackMetaData = responseXML.SelectNodes("//TrackMetaData").Item(0)?.InnerText ?? "";
				responseXML.LoadXml(trackMetaData);

				trackInfo.Artist = responseXML.GetElementsByTagName("dc:creator").Item(0)?.InnerText;
				trackInfo.Album = responseXML.GetElementsByTagName("upnp:album").Item(0)?.InnerText;
				trackInfo.TrackName = responseXML.GetElementsByTagName("dc:title").Item(0)?.InnerText;

				albumArtUri = responseXML.GetElementsByTagName("upnp:albumArtURI").Item(0)?.InnerText;
			}
			catch (Exception e)
			{
				Logger.Error(e);
				return;
			}

			TrackProgressChanged?.Invoke(this, trackProgress);

			var id = trackInfo.Artist + trackInfo.TrackName;
			if (_currentId == id)
			{
				return;
			}
			_currentId = id;

			try
			{
				var albumArtResponse = await _httpClient.GetAsync(new Uri(albumArtUri));
				if (!albumArtResponse.IsSuccessStatusCode)
				{
					Logger.Warn("Response was not successful when getting album art: " + albumArtResponse);
					trackInfo.AlbumArt = null;
				}

				var stream = await albumArtResponse.Content.ReadAsStreamAsync();
				trackInfo.AlbumArt = Image.FromStream(stream);
			}
			catch (Exception e)
			{
				Logger.Error(e);
				trackInfo.AlbumArt = null;
			}

			TrackInfoChanged?.Invoke(this, trackInfo);
		}

		private void GetCurrentPlayMode()
		{
			string response = DoWebRequest("/MediaRenderer/AVTransport/Control",
									"\"urn:schemas-upnp-org:service:AVTransport:1#GetTransportSettings\"",
									"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
									"	<s:Body>\n" +
									"		<u:GetTransportSettings xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
									"			<InstanceID>0</InstanceID>\n" +
									"		</u:GetTransportSettings>\n" +
									"	</s:Body>\n" +
									"</s:Envelope>");


			string playMode = "";
			try
			{
				XmlDocument responseXML = new XmlDocument();
				responseXML.LoadXml(response);				
				playMode = responseXML.SelectNodes("//PlayMode").Item(0)?.InnerText ?? "";
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}

			bool shuffle;
			RepeatMode repeatMode;
			switch (playMode)
			{
				case "SHUFFLE":
				case "RANDOM":
					shuffle = true;
					repeatMode = RepeatMode.RepeatContext;
					break;
				case "REPEAT_ONE":
					shuffle = _shuffle;
					repeatMode = RepeatMode.RepeatTrack;
					break;
				case "REPEAT_ALL":
					shuffle = false;
					repeatMode = RepeatMode.RepeatContext;
					break;
				default:
					shuffle = false;
					repeatMode = RepeatMode.Off;
					break;
			}

			if (shuffle != _shuffle)
			{
				_shuffle = shuffle;
				ShuffleChanged?.Invoke(this, _shuffle);
			}

			if (repeatMode != _repeatMode)
			{
				_repeatMode = repeatMode;
				RepeatModeChanged?.Invoke(this, _repeatMode);
			}
		}

		private void GetCurrentTransportState()
		{
			string response = DoWebRequest("/MediaRenderer/AVTransport/Control",
									"\"urn:schemas-upnp-org:service:AVTransport:1#GetTransportInfo\"",
									"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
									"	<s:Body>\n" +
									"		<u:GetTransportInfo xmlns:u=\"urn:schemas-upnp-org:service:AVTransport:1\">\n" +
									"			<InstanceID>0</InstanceID>\n" +
									"		</u:GetTransportInfo>\n" +
									"	</s:Body>\n" +
									"</s:Envelope>");
			
			string transportState = "";
			try
			{
				XmlDocument responseXML = new XmlDocument();
				responseXML.LoadXml(response);
				transportState = responseXML.SelectNodes("//CurrentTransportState").Item(0)?.InnerText ?? "";
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
			
			bool isPlaying = transportState == "PLAYING";
			if (isPlaying == _isPlaying)
			{
				return;
			}

			_isPlaying = isPlaying;
			IsPlayingChanged?.Invoke(this, _isPlaying);
		}

		private void GetCurrentVolume()
		{
			string response = DoWebRequest("/MediaRenderer/RenderingControl/Control",
									"\"urn:schemas-upnp-org:service:RenderingControl:1#GetVolume\"",
									"<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">\n" +
									"	<s:Body>\n" +
									"		<u:GetVolume xmlns:u=\"urn:schemas-upnp-org:service:RenderingControl:1\">\n" +
									"			<InstanceID>0</InstanceID>\n" +
									"			<Channel>Master</Channel>\n" +
									"			<CurrentVolume>0</CurrentVolume>\n" +
									"		</u:GetVolume>\n" +
									"	</s:Body>\n" +
									"</s:Envelope>");

			float volume = 1;
			try
			{
				XmlDocument responseXML = new XmlDocument();
				responseXML.LoadXml(response);
				volume = (float)Int32.Parse(responseXML.SelectNodes("//CurrentVolume").Item(0)?.InnerText ?? "1") / 100;
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}

			if (volume == _volume)
			{
				return;
			}

			_volume = volume;
			VolumeChanged?.Invoke(this, _volume);
		}
	}
}