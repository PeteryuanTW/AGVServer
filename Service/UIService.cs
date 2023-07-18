namespace AGVServer.Service
{
    public class UIService
    {
		#region laoding panel
		private PLoadingPanelData loadingPanelData = new PLoadingPanelData();
		public PLoadingPanelData GetPanelData()
		{
			return loadingPanelData;
		}
        public void ShowPanel(string msg)
		{
			loadingPanelData.panelText = msg;
			loadingPanelData.visible = true;
			PanelChanged();
		}
		public void ClosePanel()
		{
			loadingPanelData.visible = false;
			loadingPanelData.panelText = "";
			PanelChanged();
		}
		public event Action<PLoadingPanelData>? panelAct;
		private void PanelChanged()
		{
			panelAct?.Invoke(loadingPanelData);
		}
		#endregion
		#region popup
		public PPopupData popupData = new PPopupData();

        public PPopupData GetPopupData()
        {
            return popupData;
        }

        public void ShowPopup(PPopupType popupType, string content)
		{
			popupData.visible = true;
			popupData.popupType = popupType;
			popupData.content = content;
			PopupChanged();

        }
        public void ClosePopup()
        {
            popupData.visible = false;
            popupData.popupType = PPopupType.Info;
            popupData.content = "";
			PopupChanged();
        }

        public event Action<PPopupData>? popupAct;

		private void PopupChanged()
		{
			popupAct?.Invoke(popupData);
		}

		#endregion
	}

	public class PLoadingPanelData
	{
		public bool visible;
		public string panelText;

		public PLoadingPanelData()
		{
			visible = false;
			panelText = "init";
		}
	}
	public enum PPopupType { Info, Success, Warning, Dangerous }

	public class PPopupData
	{
		public bool visible;
		public PPopupType popupType;
		public string content;

		public PPopupData()
		{
			visible = false;
			popupType = PPopupType.Info;
			content = "";
		}

	}


}
