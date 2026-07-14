using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.viewModels
{
    public class VMViewSearchCustom: VMBase
    {
        private bool _isPanelContent1_isible = false;
        public bool IsPanelContent1_Visible
        {
            get => _isPanelContent1_isible;
            set
            {
                _isPanelContent1_isible = value;
                OnPropertyChanged(nameof(IsPanelContent1_Visible));
            }
        }
        private bool _isPanelContent2_isible = false;
        public bool IsPanelContent2_Visible
        {
            get => _isPanelContent2_isible;
            set
            {
                _isPanelContent2_isible = value;
                OnPropertyChanged(nameof(IsPanelContent2_Visible));
            }
        }
        private bool _isPanelContent3_isible = true;
        public bool IsPanelContent3_Visible
        {
            get => _isPanelContent3_isible;
            set
            {
                _isPanelContent3_isible = value;
                OnPropertyChanged(nameof(IsPanelContent3_Visible));
            }
        }
    }
}















