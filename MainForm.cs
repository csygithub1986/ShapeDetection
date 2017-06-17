//----------------------------------------------------------------------------
//  Copyright (C) 2004-2017 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Diagnostics;
using Emgu.CV.Util;

namespace ShapeDetection
{
    public partial class MainForm : Form
    {
        double dp = 1;    // 参数3：dp，不懂
        double minDist = 40;     // 参数4：两个圆中心的最小距离
        double cannyThreshold = 150;    // 参数5：边缘检测阈值（30~180）
        double circleAccumulatorThreshold = 30;       // 参数6：累加器阈值（圆心重合点，越低的时候圆弧就越容易当成圆）
        int minRadius = 20;
        int maxRadius = 60;

        public MainForm()
        {
            InitializeComponent();

            fileNameTextBox.Text = @"C:\Users\Csy\Desktop\新文件夹\大一点.jpg";

            txtDp.Text = dp.ToString();
            txtCandy.Text = cannyThreshold.ToString();
            txtAccThr.Text = circleAccumulatorThreshold.ToString();
            txtMinDist.Text = minDist.ToString();
            txtMinCir.Text = minRadius.ToString();
            txtMaxCir.Text = maxRadius.ToString();
        }

        public void PerformShapeDetection()
        {
            if (fileNameTextBox.Text != String.Empty)
            {
                StringBuilder msgBuilder = new StringBuilder("Performance: ");

                //Load the image from file and resize it for display
                Image<Bgr, Byte> img =
                   new Image<Bgr, byte>(fileNameTextBox.Text);
                //.Resize(2000, 2000, Emgu.CV.CvEnum.Inter.Linear, true);
                labelSize.Text = img.Width.ToString() + " , " + img.Height.ToString();
                //转为灰度级图像
                UMat uimage = new UMat();
                CvInvoke.CvtColor(img, uimage, ColorConversion.Bgr2Gray);

                //use image pyr to remove noise 降噪，为了更准确的做边缘检测
                UMat pyrDown = new UMat();
                CvInvoke.PyrDown(uimage, pyrDown);
                CvInvoke.PyrUp(pyrDown, uimage);


                #region circle detection
                Stopwatch watch = Stopwatch.StartNew();

                //// 参数1：灰度级图像
                //// 参数2：处理图像用的方法，在这里是固定
                //double dp = 1;    // 参数3：dp，不懂
                //double minDist = 40;     // 参数4：两个圆中心的最小距离
                //double cannyThreshold = 30;    // 参数5：边缘检测阈值
                //double circleAccumulatorThreshold = 30;       // 参数6：累加器阈值（圆心重合点，越低的时候圆弧就越容易当成圆）
                //int minRadius = 20;
                //int maxRadius = 60;

                CircleF[] circles = CvInvoke.HoughCircles(uimage, HoughType.Gradient, dp, minDist, cannyThreshold, circleAccumulatorThreshold, minRadius, maxRadius);

                watch.Stop();
                msgBuilder.Append(String.Format("Hough circles - {0} ms; ", watch.ElapsedMilliseconds));
                #endregion

                #region Canny and edge detection
                watch.Reset(); watch.Start();
                double cannyThresholdLinking = cannyThreshold;// 30;
                UMat cannyEdges = new UMat();
                CvInvoke.Canny(uimage, cannyEdges, cannyThreshold, cannyThresholdLinking);

                LineSegment2D[] lines = CvInvoke.HoughLinesP(
                   cannyEdges,
                   1, //Distance resolution in pixel-related units
                   Math.PI / 45.0, //Angle resolution measured in radians.
                   20, //threshold
                   30, //min Line width
                   10); //gap between lines

                watch.Stop();
                msgBuilder.Append(String.Format("Canny & Hough lines - {0} ms; ", watch.ElapsedMilliseconds));
                #endregion

                #region Find triangles and rectangles
                watch.Reset(); watch.Start();
                List<Triangle2DF> triangleList = new List<Triangle2DF>();
                List<RotatedRect> boxList = new List<RotatedRect>(); //a box is a rotated rectangle

                using (VectorOfVectorOfPoint contours = new VectorOfVectorOfPoint())
                {
                    CvInvoke.FindContours(cannyEdges, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
                    int count = contours.Size;
                    for (int i = 0; i < count; i++)
                    {
                        using (VectorOfPoint contour = contours[i])
                        using (VectorOfPoint approxContour = new VectorOfPoint())
                        {
                            CvInvoke.ApproxPolyDP(contour, approxContour, CvInvoke.ArcLength(contour, true) * 0.05, true);
                            if (CvInvoke.ContourArea(approxContour, false) > 250) //only consider contours with area greater than 250
                            {
                                if (approxContour.Size == 3) //The contour has 3 vertices, it is a triangle
                                {
                                    Point[] pts = approxContour.ToArray();
                                    triangleList.Add(new Triangle2DF(
                                       pts[0],
                                       pts[1],
                                       pts[2]
                                       ));
                                }
                                else if (approxContour.Size == 4) //The contour has 4 vertices.
                                {
                                    #region determine if all the angles in the contour are within [80, 100] degree
                                    bool isRectangle = true;
                                    Point[] pts = approxContour.ToArray();
                                    LineSegment2D[] edges = PointCollection.PolyLine(pts, true);

                                    for (int j = 0; j < edges.Length; j++)
                                    {
                                        double angle = Math.Abs(
                                           edges[(j + 1) % edges.Length].GetExteriorAngleDegree(edges[j]));
                                        if (angle < 80 || angle > 100)
                                        {
                                            isRectangle = false;
                                            break;
                                        }
                                    }
                                    #endregion

                                    if (isRectangle) boxList.Add(CvInvoke.MinAreaRect(approxContour));
                                }
                            }
                        }
                    }
                }

                watch.Stop();
                msgBuilder.Append(String.Format("Triangles & Rectangles - {0} ms; ", watch.ElapsedMilliseconds));
                #endregion

                originalImageBox.Image = img;
                this.Text = msgBuilder.ToString();

                #region draw triangles and rectangles
                Mat triangleRectangleImage = new Mat(img.Size, DepthType.Cv8U, 3);
                triangleRectangleImage.SetTo(new MCvScalar(0));
                foreach (Triangle2DF triangle in triangleList)
                {
                    CvInvoke.Polylines(triangleRectangleImage, Array.ConvertAll(triangle.GetVertices(), Point.Round), true, new Bgr(Color.DarkBlue).MCvScalar, 2);
                }
                foreach (RotatedRect box in boxList)
                {
                    CvInvoke.Polylines(triangleRectangleImage, Array.ConvertAll(box.GetVertices(), Point.Round), true, new Bgr(Color.DarkOrange).MCvScalar, 2);
                }

                triangleRectangleImageBox.Image = cannyEdges;
                #endregion

                #region draw circles
                Mat circleImage = new Mat(img.Size, DepthType.Cv8U, 3);
                circleImage.SetTo(new MCvScalar(0));
                foreach (CircleF circle in circles)
                    CvInvoke.Circle(circleImage, Point.Round(circle.Center), (int)circle.Radius, new Bgr(Color.Brown).MCvScalar, 2);

                circleImageBox.Image = circleImage;
                #endregion

                #region draw lines
                Mat lineImage = new Mat(img.Size, DepthType.Cv8U, 3);
                lineImage.SetTo(new MCvScalar(0));
                foreach (LineSegment2D line in lines)
                    CvInvoke.Line(lineImage, line.P1, line.P2, new Bgr(Color.Green).MCvScalar, 2);

                lineImageBox.Image = uimage;
                #endregion
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            PerformShapeDetection();
        }

        private void loadImageButton_Click(object sender, EventArgs e)
        {
            DialogResult result = openFileDialog1.ShowDialog();
            if (result == DialogResult.OK || result == DialogResult.Yes)
            {
                fileNameTextBox.Text = openFileDialog1.FileName;
            }
        }

        /// <summary>
        /// 调整参数后重新检测
        /// </summary>
        private void button1_Click(object sender, EventArgs e)
        {
            dp = int.Parse(txtDp.Text.Trim());
            cannyThreshold = int.Parse(txtCandy.Text.Trim());
            circleAccumulatorThreshold = int.Parse(txtAccThr.Text.Trim());
            minDist = int.Parse(txtMinDist.Text.Trim());
            minRadius = int.Parse(txtMinCir.Text.Trim());
            maxRadius = int.Parse(txtMaxCir.Text.Trim());

            PerformShapeDetection();
        }
    }
}
