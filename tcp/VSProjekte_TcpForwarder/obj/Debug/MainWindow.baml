   M S B A M L   `   `   `  ����[  WSystem.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089Y@clr-namespace:System.Windows.Forms;assembly=System.Windows.FormsSystem.Windows.Forms  5   	    TcpForwarder   TcpForwarder.MainWindow   5   	   W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35^ ZWindowsFormsIntegration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35F 9http://schemas.microsoft.com/winfx/2006/xaml/presentation     R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	   RForms@clr-namespace:System.Windows.Forms;assembly=System.Windows.Forms     5   	   -   5          � Title$  TCP Forwarder��5   	   $��642.55��6   $
��1270��6/   #��� ��6<   &   System.Windows.Media.TextOptions   TextFormattingMode$ Display=�6�   .��5      � .+�5   
   � $��0,97,0,0q�6    � ColumnDefinitions 5      �� 5	      $��1* �6#   �� 5
      $��1* �6#   .+�5      �� -   5         ��Name$ 	logClient��5      $��10,25,10,10q�6-   " �� VerticalScrollBarVisibility$ Visible=�6B    �� 
IsReadOnly . . �5      /� 5      �� 5      -   5        $ 	logServer��5      $��1��6-   $��10,25,10,10q�6=   $ Visible=�6R    . . �5      /� 5      �� 5      -   5        $��Clear��5      $��0,0,10,0q�6%   $	��Top=�67   $��Right=�6O   $��76��6k   6v   �� 5      -   5        $��Clear��5      $��0,0,10,0q�6%   $	��Top=�67   $��Right=�6O   $��76��6k   $��1��6v   6�   �� 5      -   5         ��Name$ checkPauseClient��5      $��Pause��61   $��	0,3,345,0q�6A   $	��Top=�6T   {�.  $��Right=�6�   $��51��6�   �� 5      -   5        $ checkPauseServer��5      $��Pause��61   $��	0,3,345,0q�6A   $	��Top=�6T   $��1��6l   {�.  $��Right=�6�   $��51��6�   7  1System.Windows.Forms.Integration.WindowsFormsHost  5      -   5          Name$ 
formsHost1��5      $��23��63   $��	0,0,124,0q�6?   $	��Top=�6R   $��Right=�6j   $��60��6�      Child. 5      (   "System.Windows.Forms.NumericUpDown  5        5      -	   5        $ 
formsHost2��5      $��23��63   $��	0,0,124,0q�6?   $	��Top=�6R   $��1��6j   $��Right=�6z   $��60��6�   . 5        5      �� 5      -
   5        $ checkLimitSpeedClient��5      $��Limit Speed (KiB/s):��66   $��	0,3,201,0q�6U   $	��Top=�6h   {�.  $��Right=�6�   $	��123��6   �� 5      -   5        $ checkLimitSpeedServer��5      $��Limit Speed (KiB/s):��66   $��	0,3,201,0q�6U   $	��Top=�6h   $��1��6�   {�.  $��Right=�6�   $	��123��6  �� 5      $��Server ⇒ Client:��6   $��1��6/   $
��Left=�6?   $��	10,-3,0,0q�6Z   $	��Top=�6m   �� 5      $��Client ⇒ Server:��6   $
��Left=�6/   $��	10,-3,0,0q�6J   $	��Top=�6]   5!      �� 5"   
   $��Remote host:��6   $
��Left=�6'   $��	10,10,0,0q�6B   $	��Top=�6U   �� 5#   
   -   5        	 ��Name$	 textHost��5#      $
��Left=�6$   $��	95,14,0,0q�6?   
 �� TextWrapping$

 Wrap=�6R   $	��Top=�6f   $	��159��6~   $��[localhost-random]��6�   �� 5$   
   $��Remote port:��6   $
��Left=�6'   $��
259,10,0,0q�6B   $	��Top=�6V   �� 5%   
   -   5        $	 textPortRemote��5%      $
��Left=�6*   $��
343,14,0,0q�6E   $

 Wrap=�6Y   $	��Top=�6m   $��55��6�   $
��8080��6�   �� 5&   
   $��Local port:��6   $
��Left=�6&   $��
424,10,0,0q�6A   $	��Top=�6U   �� 5'   
   -   5        $	 textPortLocal��5'      $
��Left=�6)   $��
494,14,0,0q�6D   $

 Wrap=�6X   $	��Top=�6l   $��55��6�   $
��8081��6�   �� 5(   
   -   5         ��Name$ startButton��5(      $��Start��6&   $��Right=�66   $��	0,13,10,0q�6R   $	��Top=�6e   $��60��6}   6�   �� 5)   
   -   5         ��Name$ labelConnectionsCount��5)      $��xx Connections.��6/   $��	10,41,0,0q�6I   $	��Top=�6\    J� RenderTransformOrigin$ 0.5,0.5&�6t   $
��Left=�6�   �� 5*   
   -   5        $ labelUlSpeed��5*      $��UL: xxx,xx MiB/s��6&   $
��Left=�6A   $��	10,67,0,0q�6\   $	��Top=�6o   �� 5+   
   -   5        $ labelDlSpeed��5+      $��DL: xxx,xx MiB/s��6&   $
��Left=�6A   $��
126,67,0,0q�6\   $	��Top=�6p   �� 5,   
   -   5        $ checkLogging��5,      $��Log control events��6)   $
��Left=�6F   $��
177,47,0,0q�6a   $	��Top=�6u   6�   �� 5-   
   -   5        $ checkLogDataEvents��5-      $��Log data events��6/   $
��Left=�6I   $��
318,47,0,0q�6d   $	��Top=�6x   {�.  6�   �� 5.   
   -   5        $ checkShowDataContents��5.      $��Log data contents��62   $
��Left=�6N   $��
449,47,0,0q�6i   $	��Top=�6}   {�.  6�   �� 5/   
   -   5        $ checkUseSsl��5/      $'��!Use SSL/TLS for remote connection��6(   $
��Left=�6T   $��	582,3,0,0q�6o   $	��Top=�6�   6�   �� 50   
   -   5        $ checkLogToFile��50      $.��(Log data contents to files (current dir)��6+   $��	0,16,84,0q�6^   $	��Top=�6q   $��Right=�6�   �� 51   
   -   5        $ checkUseServerSsl��51      $*��$Provide SSL/TLS for local connection��6.   $
��Left=�6]   $��
582,23,0,0q�6x   $	��Top=�6�   6�   53      54      