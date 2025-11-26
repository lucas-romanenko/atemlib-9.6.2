using BMDSwitcherAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SwitcherLib
{
    public class Switcher
    {
        protected IBMDSwitcher switcher;
        protected String deviceAddress;
        protected bool connected;

        public Switcher(string deviceAddress)
        {
            this.deviceAddress = deviceAddress;
        }

        public IBMDSwitcher GetSwitcher()
        {
            return this.switcher;
        }

        public void Connect()
        {
            if (this.connected)
            {
                return;
            }

            IBMDSwitcherDiscovery switcherDiscovery = new CBMDSwitcherDiscovery();
            _BMDSwitcherConnectToFailure failReason = 0;

            try
            {
                switcherDiscovery.ConnectTo(this.deviceAddress, out this.switcher, out failReason);
                this.connected = true;
            }
            catch (COMException ex)
            {
                switch (failReason)
                {
                    case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureIncompatibleFirmware:
                        throw new SwitcherLibException("Incompatible firmware");

                    case _BMDSwitcherConnectToFailure.bmdSwitcherConnectToFailureNoResponse:
                        throw new SwitcherLibException(String.Format("No response from {0}", this.deviceAddress));

                    default:
                        throw new SwitcherLibException(String.Format("Unknown Error: {0}", ex.Message));
                }
            }
            catch (Exception ex)
            {
                throw new SwitcherLibException(String.Format("Unable to connect to switcher: {0}", ex.Message));
            }
        }

        public String GetProductName()
        {
            this.Connect();
            String productName;
            this.switcher.GetProductName(out productName);
            return productName;
        }

        public int GetVideoHeight()
        {
            this.Connect();
            _BMDSwitcherVideoMode videoMode;
            this.switcher.GetVideoMode(out videoMode);
            _BMDSwitcherVideoMode switcherVideoMode = videoMode;
            switch (switcherVideoMode)
            {
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode4KHDp2398:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode4KHDp24:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode4KHDp25:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode4KHDp2997:
                    return 2160;

                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode720p50:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode720p5994:
                    return 720;

                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080i50:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080i5994:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p50:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p2398:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p24:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p25:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p2997:
                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode1080p5994:
                    return 1080;

                case _BMDSwitcherVideoMode.bmdSwitcherVideoMode525i5994NTSC:
                    return 480;
                default:
                    throw new SwitcherLibException(String.Format("Unsupported resolution: {0}", videoMode.ToString()));
            }
        }

        public int GetVideoWidth()
        {
            int videoHeight = this.GetVideoHeight();
            switch (videoHeight)
            {
                case 720:
                    return 1280;

                case 1080:
                    return 1920;

                case 2160:
                    return 3840;

                case 480:
                    return 720;
                default:
                    throw new SwitcherLibException(String.Format("Unsupported video height: {0}", videoHeight.ToString()));
            }
        }

        /// <summary>
        /// Check if a slot is safe to upload to.
        /// Returns true if safe (slot not on a media player with tally ON).
        /// Returns false if unsafe (slot is on a media player that's currently on program).
        /// </summary>
        public bool IsSlotSafeToUpload(int slotIndex)
        {
            this.Connect();

            try
            {
                // Get all media players and check if any have this slot loaded
                IntPtr mediaPlayerIteratorPtr;
                Guid mediaIteratorIID = typeof(IBMDSwitcherMediaPlayerIterator).GUID;
                this.switcher.CreateIterator(ref mediaIteratorIID, out mediaPlayerIteratorPtr);
                IBMDSwitcherMediaPlayerIterator mediaPlayerIterator = (IBMDSwitcherMediaPlayerIterator)Marshal.GetObjectForIUnknown(mediaPlayerIteratorPtr);

                // Get program input to check tally
                long programInput = GetProgramInput();

                IBMDSwitcherMediaPlayer mediaPlayer;
                mediaPlayerIterator.Next(out mediaPlayer);
                int mediaPlayerNumber = 1;

                while (mediaPlayer != null)
                {
                    _BMDSwitcherMediaPlayerSourceType type;
                    uint index;
                    mediaPlayer.GetSource(out type, out index);

                    // Check if this media player has our slot loaded
                    if (type == _BMDSwitcherMediaPlayerSourceType.bmdSwitcherMediaPlayerSourceTypeStill && (int)index == slotIndex)
                    {
                        // Slot is on this media player - check if media player is on program
                        long mediaPlayerInputId = GetMediaPlayerInputId(mediaPlayerNumber);
                        
                        if (mediaPlayerInputId == programInput)
                        {
                            Log.Debug(String.Format("Slot {0} is on Media Player {1} which is ON PROGRAM - NOT SAFE", slotIndex + 1, mediaPlayerNumber));
                            return false;
                        }
                        else
                        {
                            Log.Debug(String.Format("Slot {0} is on Media Player {1} but not on program - SAFE", slotIndex + 1, mediaPlayerNumber));
                        }
                    }

                    mediaPlayerNumber++;
                    mediaPlayerIterator.Next(out mediaPlayer);
                }

                // Slot is not on any media player that's on program
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(String.Format("Error checking slot safety: {0}", ex.Message));
                // If we can't check, assume unsafe
                return false;
            }
        }

        /// <summary>
        /// Get the current program input ID from Mix Effect Block 1
        /// </summary>
        private long GetProgramInput()
        {
            IntPtr meIteratorPtr;
            Guid meIteratorIID = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
            this.switcher.CreateIterator(ref meIteratorIID, out meIteratorPtr);
            IBMDSwitcherMixEffectBlockIterator meIterator = (IBMDSwitcherMixEffectBlockIterator)Marshal.GetObjectForIUnknown(meIteratorPtr);

            IBMDSwitcherMixEffectBlock mixEffectBlock;
            meIterator.Next(out mixEffectBlock);

            if (mixEffectBlock != null)
            {
                long programInput;
                mixEffectBlock.GetProgramInput(out programInput);
                return programInput;
            }

            return -1;
        }

        /// <summary>
        /// Get the input ID for a media player (1-based media player number)
        /// Media Player 1 = 3010, Media Player 2 = 3020, etc.
        /// </summary>
        private long GetMediaPlayerInputId(int mediaPlayerNumber)
        {
            // Standard ATEM input IDs for media players
            // MP1 = 3010, MP2 = 3020, MP3 = 3030, MP4 = 3040
            return 3000 + (mediaPlayerNumber * 10);
        }

        public IList<MediaStill> GetStills()
        {
            IList<MediaStill> list = new List<MediaStill>();

            IBMDSwitcherMediaPool switcherMediaPool = (IBMDSwitcherMediaPool)this.switcher;

            IBMDSwitcherStills stills;
            switcherMediaPool.GetStills(out stills);

            uint count;
            stills.GetCount(out count);
            for (uint index = 0; index < count; index++)
            {
                MediaStill mediaStill = new MediaStill(stills, index);
                list.Add(mediaStill);
            }

            IntPtr mediaPlayerIteratorPtr;
            Guid mediaIteratorIID = typeof(IBMDSwitcherMediaPlayerIterator).GUID;
            this.switcher.CreateIterator(ref mediaIteratorIID, out mediaPlayerIteratorPtr);
            IBMDSwitcherMediaPlayerIterator mediaPlayerIterator = (IBMDSwitcherMediaPlayerIterator)Marshal.GetObjectForIUnknown(mediaPlayerIteratorPtr);

            IBMDSwitcherMediaPlayer mediaPlayer;
            mediaPlayerIterator.Next(out mediaPlayer);
            int num1 = 1;
            while (mediaPlayer != null)
            {
                _BMDSwitcherMediaPlayerSourceType type;
                uint index;
                mediaPlayer.GetSource(out type, out index);
                if (type == _BMDSwitcherMediaPlayerSourceType.bmdSwitcherMediaPlayerSourceTypeStill)
                {
                    int num2 = (int)index + 1;
                    foreach (MediaStill mediaStill in list)
                    {
                        if (mediaStill.Slot == num2)
                        {
                            mediaStill.MediaPlayer = num1;
                            break;
                        }
                    }
                }
                num1++;
                mediaPlayerIterator.Next(out mediaPlayer);
            }
            return list;
        }

    }
}
