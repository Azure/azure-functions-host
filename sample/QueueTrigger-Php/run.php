<?php
  $input = file_get_contents(getenv('input'));
  $input = rtrim($input, "\n\r");
  fwrite(STDOUT, "PHP script processed queue message '$input'");
?>