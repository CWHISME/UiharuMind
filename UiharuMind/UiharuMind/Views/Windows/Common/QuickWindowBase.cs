using System;
using UiharuMind.Utils;

namespace UiharuMind.Views.Common;

public class QuickWindowBase : UiharuWindowBase
{
    public override void Awake()
    {
        this.SetSimpledecorationWindow();
    }

    public void PlayOpenAnimation()
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, true, null);
    }

    public void CloseByAnimation()
    {
        UiAnimationUtils.PlayAlphaTransitionAnimation(this, false, SafeClose);
    }
}