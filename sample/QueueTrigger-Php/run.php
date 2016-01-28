<?php
  $input = fgets(STDIN);
  $input = rtrim($input, "\n\r");
  fwrite(STDOUT, "PHP script processed queue message '$input'");
?>